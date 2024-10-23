using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Api.Implementations.Common;
using Core.Entities;
using Core.Interfaces;
using DotNext.Runtime;
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

    public async Task<IEnumerable<IGrouping<byte[], File>>> FindSimilarFilesAsync(string[] hypotheticalDuplicates, PerceptualHashAlgorithm? perceptualHashAlgorithm = null,
        int? degreeOfSimilarity = null, CancellationToken cancellationToken = default)
    {
        // Partial hash generation
        var partialDuplicates =
            new ConcurrentDictionary<byte[], ConcurrentStack<File>>(Environment.ProcessorCount,
                hypotheticalDuplicates.Length, new HashComparer());

        var progressChannel = Channel.CreateUnboundedPrioritized(new UnboundedPrioritizedChannelOptions<int>
            { SingleWriter = false, SingleReader = true });

        try
        {
            await Task.WhenAll(SendProgress(progressChannel.Reader, 0.1m, cancellationToken),
                SetPartialDuplicates(hypotheticalDuplicates, partialDuplicates, 0.1m,
                    progressChannel.Writer, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            partialDuplicates.Clear();
            return [];
        }

        hypotheticalDuplicates = partialDuplicates.Where(group => group.Value.Count > 1)
            .SelectMany(group => group.Value.Select(file => file.Path)).ToArray();

        partialDuplicates.Clear();

        // Full hash generation
        progressChannel = Channel.CreateUnboundedPrioritized(new UnboundedPrioritizedChannelOptions<int>
            { SingleWriter = false, SingleReader = true });

        try
        {
            await Task.WhenAll(SendProgress(progressChannel.Reader, 1, cancellationToken),
                SetPartialDuplicates(hypotheticalDuplicates, partialDuplicates, 1,
                    progressChannel.Writer, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            partialDuplicates.Clear();
            return [];
        }

        return partialDuplicates.Where(group => group.Value.Count > 1).SelectMany(group =>
                group.Value.OrderByDescending(file => file.DateModified)
                    .Select(files => new { group.Key, Value = files }))
            .ToLookup(group => group.Key, group => group.Value);
    }

    private async Task SetPartialDuplicates(string[] hypotheticalDuplicates,
        ConcurrentDictionary<byte[], ConcurrentStack<File>> partialDuplicates, decimal percentageOfFileToHash,
        ChannelWriter<int> progressWriter, CancellationToken cancellationToken)
    {
        var progress = 0;

        await Parallel.ForAsync<nuint>(0, hypotheticalDuplicates.GetLength(),
            new ParallelOptions
                { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken },
            async (i, hashingToken) =>
            {
                try
                {
                    using var fileHandle = FileReader.GetFileHandle(hypotheticalDuplicates[i], true, true);

                    var size = RandomAccess.GetLength(fileHandle);

                    var bytesToHash = Convert.ToInt64(decimal.Round(decimal.Multiply(size, percentageOfFileToHash),
                        MidpointRounding.ToPositiveInfinity));

                    var hash = await _hashGenerator.GenerateHash(fileHandle, bytesToHash, hashingToken);

                    if (hash == null)
                    {
                        _ = SendError($"File {hypotheticalDuplicates[i]} is corrupted", _notificationContext,
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

                    var group = partialDuplicates.GetOrAdd(file.Hash, []);
                    group.Push(file);

                    var current = Interlocked.Increment(ref progress);

                    progressWriter.TryWrite(current);
                }
                catch (IOException)
                {
                    _ = SendError($"File {hypotheticalDuplicates[i]} is already being used by another application",
                        _notificationContext, hashingToken);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            });

        progressWriter.Complete();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task SendError(string message, IHubContext<NotificationHub> notificationContext,
        CancellationToken cancellationToken)
    {
        return notificationContext.Clients.All.SendAsync("notify",
            new Notification(NotificationType.Exception, message), cancellationToken);
    }

    public async Task SendProgress(ChannelReader<int> progressReader, decimal percentageOfFileToHash,
        CancellationToken cancellationToken)
    {
        var progress = 0;
        await foreach (var hashProcessed in progressReader.ReadAllAsync(cancellationToken))
        {
            progress = hashProcessed;

            if (progress % 100 != 0)
                continue;

            await _notificationContext.Clients.All.SendAsync("notify",
                new Notification(percentageOfFileToHash switch
                {
                    0.1m => NotificationType.HashGenerationProgress,
                    _ => NotificationType.TotalProgress
                }, progress.ToString()), cancellationToken);
        }

        await _notificationContext.Clients.All.SendAsync("notify",
            new Notification(percentageOfFileToHash switch
            {
                0.1m => NotificationType.HashGenerationProgress,
                _ => NotificationType.TotalProgress
            }, progress.ToString()), cancellationToken);
    }
}