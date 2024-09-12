using System.Collections.Concurrent;
using System.Threading.Channels;
using Api.Implementations.Common;
using CommunityToolkit.HighPerformance.Buffers;
using Core.Entities;
using Core.Interfaces;
using DotNext.Threading;
using Microsoft.AspNetCore.SignalR;
using File = Core.Entities.File;

namespace Api.Implementations.DuplicatesByHash;

public class DuplicateByHashFinder : ISimilarFilesFinder
{
    private readonly IHashGenerator _hashGenerator;
    private readonly IHubContext<NotificationHub> _notificationContext;

    public DuplicateByHashFinder(IHashGenerator hashGenerator, IHubContext<NotificationHub> notificationContext)
    {
        _hashGenerator = hashGenerator;
        _notificationContext = notificationContext;
    }

    public PerceptualHashAlgorithm PerceptualHashAlgorithm { get; set; }
    public int DegreeOfSimilarity { get; set; }

    public async Task<IEnumerable<IGrouping<string, File>>> FindSimilarFilesAsync(
        string[] hypotheticalDuplicates, CancellationToken cancellationToken = default)
    {
        // Partial hash generation
        var partialDuplicates =
            new ConcurrentDictionary<string, ConcurrentStack<File>>(Environment.ProcessorCount,
                hypotheticalDuplicates.Length);

        var progressChannel = Channel.CreateBounded<int>(new BoundedChannelOptions(1)
            { SingleWriter = false, SingleReader = true, FullMode = BoundedChannelFullMode.DropNewest});

        var progressTask = SendProgress(progressChannel.Reader, 0.1m, cancellationToken);

        using var partialHashGenerationTask =
            SetPartialDuplicates(hypotheticalDuplicates, partialDuplicates, 0.1m, progressChannel.Writer,
                cancellationToken);

        if (partialHashGenerationTask.IsCanceled || progressTask.IsCanceled)
            return [];

        await Task.WhenAll(await partialHashGenerationTask, progressTask);

        hypotheticalDuplicates = partialDuplicates.Where(group => group.Value.Count > 1)
            .SelectMany(group => group.Value.Select(file => file.Path)).ToArray();

        partialDuplicates.Clear();

        // Full hash generation
        progressChannel = Channel.CreateBounded<int>(new BoundedChannelOptions(1)
            { SingleWriter = false, SingleReader = true, FullMode = BoundedChannelFullMode.DropNewest });

        progressTask = SendProgress(progressChannel.Reader, 1, cancellationToken);

        using var fullHashGenerationTask =
            SetPartialDuplicates(hypotheticalDuplicates, partialDuplicates, 1, progressChannel.Writer,
                cancellationToken);

        if (fullHashGenerationTask.IsCanceled || progressTask.IsCanceled)
            return [];

        await Task.WhenAll(fullHashGenerationTask, progressTask);

        progressTask.Dispose();

        return partialDuplicates.Where(group => group.Value.Count > 1).SelectMany(group =>
                group.Value.OrderByDescending(file => file.DateModified)
                    .Select(files => new { group.Key, Value = files }))
            .ToLookup(group => group.Key, group => group.Value);
    }

    private async Task<Task> SetPartialDuplicates(string[] hypotheticalDuplicates,
        ConcurrentDictionary<string, ConcurrentStack<File>> partialDuplicates, decimal percentageOfFileToHash,
        ChannelWriter<int> progressWriter, CancellationToken cancellationToken)
    {
        StringPool.Shared.Reset();

        var progress = 0;

        var hashGenerationTask = Parallel.ForAsync(0, hypotheticalDuplicates.Length,
            new ParallelOptions
                { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken },
            async (i, hashingToken) =>
            {
                try
                {
                    using var fileHandle = FileReader.GetFileHandle(hypotheticalDuplicates[i], true);

                    var size = RandomAccess.GetLength(fileHandle);

                    var bytesToHash = Convert.ToInt64(decimal.Round(decimal.Multiply(size, percentageOfFileToHash),
                        MidpointRounding.ToPositiveInfinity));

                    var hash = _hashGenerator.GenerateHash(fileHandle, bytesToHash, hashingToken);

                    if (string.IsNullOrEmpty(hash))
                    {
                        await SendError($"File {hypotheticalDuplicates[i]} is corrupted", _notificationContext,
                            hashingToken);

                        return;
                    }

                    var file = new File
                    {
                        Path = hypotheticalDuplicates[i],
                        DateModified = System.IO.File.GetLastWriteTime(fileHandle),
                        Size = size,
                        Hash = hash
                    };

                    partialDuplicates.AddOrUpdate(file.Hash, [], (_, files) =>
                    {
                        files.Push(file);
                        return files;
                    });

                    progressWriter.TryWrite(Atomic.UpdateAndGet(ref progress, val => val + 1));
                }
                catch (IOException)
                {
                    await SendError($"File {hypotheticalDuplicates[i]} is already being used by another application",
                        _notificationContext, hashingToken);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            });

        if (hashGenerationTask.IsCanceled)
            return Task.FromException(new OperationCanceledException());

        await hashGenerationTask;
        progressWriter.Complete();
        return Task.CompletedTask;
    }

    public static Task SendError(string message, IHubContext<NotificationHub> notificationContext,
        CancellationToken cancellationToken)
    {
        return notificationContext.Clients.All.SendAsync("notify",
            new Notification(NotificationType.Exception, message), cancellationToken);
    }

    public async Task SendProgress(ChannelReader<int> progressReader, decimal percentageOfFileToHash,
        CancellationToken cancellationToken)
    {
        await foreach (var hashProcessed in progressReader.ReadAllAsync(cancellationToken))
        {
            await _notificationContext.Clients.All.SendAsync("notify", new Notification(percentageOfFileToHash switch
            {
                0.1m => NotificationType.HashGenerationProgress,
                _ => NotificationType.TotalProgress
            }, hashProcessed.ToString()), cancellationToken);
        }
    }
}