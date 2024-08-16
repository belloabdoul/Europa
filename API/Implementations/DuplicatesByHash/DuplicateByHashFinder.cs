using System.Collections.Concurrent;
using System.Threading.Channels;
using API.Implementations.Common;
using Core.Entities;
using Core.Interfaces;
using Microsoft.AspNetCore.SignalR;
using File = Core.Entities.File;

namespace API.Implementations.DuplicatesByHash;

public class DuplicateByHashFinder : ISimilarFilesFinder
{
    private readonly IHashGenerator _hashGenerator;
    private readonly IHubContext<NotificationHub> _notificationContext;

    public DuplicateByHashFinder(IHashGenerator hashGenerator,
        IHubContext<NotificationHub> notificationContext)
    {
        _hashGenerator = hashGenerator;
        _notificationContext = notificationContext;
    }

    public int DegreeOfSimilarity { get; set; }

    public async Task<IEnumerable<IGrouping<string, File>>> FindSimilarFilesAsync(
        string[] hypotheticalDuplicates, CancellationToken cancellationToken = default)
    {
        var progressChannel = Channel.CreateBounded<Notification>(new BoundedChannelOptions(1)
            { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.DropNewest });

        // Partial hash generation
        using var partialHashProgressTask =
            SendProgress(progressChannel, _notificationContext, cancellationToken);

        using var partialHashGenerationTask =
            SetPartialDuplicates(hypotheticalDuplicates, progressChannel, 10, cancellationToken);

        await Task.WhenAll(partialHashGenerationTask, partialHashProgressTask);

        if (partialHashGenerationTask.IsCanceled || partialHashProgressTask.IsCanceled)
            return [];

        var duplicates = partialHashGenerationTask.GetAwaiter().GetResult();

        hypotheticalDuplicates = duplicates.Where(group => group.Value.Count > 1)
            .SelectMany(group => group.Value.Select(file => file.Path)).ToArray();

        duplicates.Clear();

        // Full hash generation
        progressChannel = Channel.CreateBounded<Notification>(new BoundedChannelOptions(1)
            { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.DropNewest });

        using var fullHashProgressTask =
            SendProgress(progressChannel, _notificationContext, cancellationToken);

        using var fullHashGenerationTask =
            SetPartialDuplicates(hypotheticalDuplicates, progressChannel.Writer, 1, cancellationToken);

        await Task.WhenAll(fullHashGenerationTask, fullHashProgressTask);

        if (fullHashGenerationTask.IsCanceled || fullHashProgressTask.IsCanceled)
            return [];

        duplicates = fullHashGenerationTask.GetAwaiter().GetResult();

        return duplicates.Where(group => group.Value.Count > 1).SelectMany(group =>
                group.Value.OrderByDescending(file => file.DateModified)
                    .Select(files => new { group.Key, Value = files }))
            .ToLookup(group => group.Key, group => group.Value);
    }

    private async Task<ConcurrentDictionary<string, ConcurrentQueue<File>>> SetPartialDuplicates(
        string[] hypotheticalDuplicates, ChannelWriter<Notification> progressChannelWriter, long lengthDivisor,
        CancellationToken cancellationToken)
    {
        var progress = 0;
        var partialDuplicates =
            new ConcurrentDictionary<string, ConcurrentQueue<File>>(Environment.ProcessorCount,
                hypotheticalDuplicates.Length);

        await Parallel.ForAsync(0, hypotheticalDuplicates.Length,
            new ParallelOptions
                { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Environment.ProcessorCount / 2 },
            async (i, similarityToken) =>
            {
                var added = await GenerateHashAsync(hypotheticalDuplicates[i], lengthDivisor, partialDuplicates,
                    _hashGenerator, _notificationContext, similarityToken);

                if (!added)
                    return;

                var current = Interlocked.Increment(ref progress);

                await progressChannelWriter.WriteAsync(new Notification(lengthDivisor switch
                {
                    10 => NotificationType.HashGenerationProgress,
                    _ => NotificationType.TotalProgress
                }, current.ToString()), similarityToken);

                if (current % 1000 == 0)
                    GC.Collect(2, GCCollectionMode.Default, false, true);
            });

        progressChannelWriter.Complete();

        return partialDuplicates;
    }

    public async Task<bool> GenerateHashAsync(string hypotheticalDuplicate, long lengthDivisor,
        ConcurrentDictionary<string, ConcurrentQueue<File>> partialDuplicates, IHashGenerator hashGenerator,
        IHubContext<NotificationHub> notificationContext, CancellationToken cancellationToken)
    {
        try
        {
            using var fileHandle =
                FileReader.GetFileHandle(hypotheticalDuplicate, true, true);

            var size = RandomAccess.GetLength(fileHandle);

            var bytesToHash = Convert.ToInt64(Math.Round(decimal.Divide(size, lengthDivisor),
                MidpointRounding.ToPositiveInfinity));

            var hash = await hashGenerator.GenerateHashAsync(fileHandle, bytesToHash,
                cancellationToken);

            if (string.IsNullOrEmpty(hash))
            {
                await SendError($"File {hypotheticalDuplicate} is corrupted", notificationContext,
                    cancellationToken);
                return false;
            }

            var file = new File
            {
                Path = hypotheticalDuplicate,
                DateModified = System.IO.File.GetLastWriteTime(hypotheticalDuplicate),
                Size = size,
                Hash = hash
            };

            var group = partialDuplicates.GetOrAdd(file.Hash, new ConcurrentQueue<File>());


            group.Enqueue(file);

            return true;
        }
        catch (IOException)
        {
            await SendError($"File {hypotheticalDuplicate} is already being used by another application",
                notificationContext, cancellationToken);
        }

        return false;
    }

    public async Task SendError(string message, IHubContext<NotificationHub> notificationContext,
        CancellationToken cancellationToken)
    {
        await notificationContext.Clients.All.SendAsync("notify", new Notification(NotificationType.Exception, message), cancellationToken);
    }

    public async Task SendProgress(ChannelReader<Notification> progressChannelReader,
        IHubContext<NotificationHub> notificationContext, CancellationToken cancellationToken)
    {
        await foreach (var progress in progressChannelReader.ReadAllAsync(cancellationToken))
            await notificationContext.Clients.All.SendAsync("notify", progress, cancellationToken);
    }
}