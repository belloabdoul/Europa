using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Api.Implementations.Commons;
using CommunityToolkit.HighPerformance.Buffers;
using Core.Entities.Commons;
using Core.Entities.Notifications;
using Core.Interfaces.Commons;
using Microsoft.AspNetCore.SignalR;
using File = Core.Entities.Files.File;

namespace Api.Implementations.DuplicatesByHash;

public class DuplicateByHashFinder(IHashGenerator hashGenerator, IHubContext<NotificationHub> notificationContext)
    : ISimilarFilesFinder
{
    public async Task<IEnumerable<IGrouping<byte[], File>>> FindSimilarFilesAsync(
        List<string> hypotheticalDuplicates,
        decimal? degreeOfSimilarity = null, CancellationToken cancellationToken = default)
    {
        // Partial hash generation
        var partialDuplicates =
            new ConcurrentDictionary<byte[], ConcurrentStack<File>>(Environment.ProcessorCount,
                hypotheticalDuplicates.Count, new ByteArrayComparer());

        var progressChannel = Channel.CreateUnboundedPrioritized(new UnboundedPrioritizedChannelOptions<int>
            { SingleWriter = false, SingleReader = true });

        try
        {
            await Task.WhenAll(SendProgress(progressChannel.Reader, 0.1m, cancellationToken),
                SetPartialDuplicates(hypotheticalDuplicates, partialDuplicates, 0.1m, hashGenerator,
                    notificationContext, progressChannel.Writer, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            partialDuplicates.Clear();
            return [];
        }

        hypotheticalDuplicates =
        [
            ..partialDuplicates.Where(group => group.Value.Count > 1)
                .SelectMany(group => group.Value.Select(file => file.Path))
        ];

        partialDuplicates.Clear();

        // Full hash generation
        progressChannel = Channel.CreateUnboundedPrioritized(new UnboundedPrioritizedChannelOptions<int>
            { SingleWriter = false, SingleReader = true });

        try
        {
            await Task.WhenAll(SendProgress(progressChannel.Reader, 1, cancellationToken),
                SetPartialDuplicates(hypotheticalDuplicates, partialDuplicates, 1, hashGenerator, notificationContext,
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

    private static async Task SetPartialDuplicates(List<string> hypotheticalDuplicates,
        ConcurrentDictionary<byte[], ConcurrentStack<File>> partialDuplicates, decimal percentageOfFileToHash,
        IHashGenerator hashGenerator, IHubContext<NotificationHub> notificationContext,
        ChannelWriter<int> progressWriter, CancellationToken cancellationToken)
    {
        using var progress = MemoryOwner<int>.Allocate(1, ArrayPool<int>.Shared);
        progress.Span.Clear();
        await Parallel.ForAsync(0, hypotheticalDuplicates.Count, cancellationToken,
            (index, hashingToken) => GenerateHashAsync(hypotheticalDuplicates[index], hashGenerator,
                percentageOfFileToHash, partialDuplicates, notificationContext, progress.Memory, progressWriter,
                hashingToken));

        progressWriter.TryWrite(progress.DangerousGetReference());
        progressWriter.Complete();
    }

    private static long GetLengthToHash(long fileSize, decimal percentageOfFileToHash)
    {
        return Convert.ToInt64(decimal.Round(decimal.Multiply(fileSize, percentageOfFileToHash),
            MidpointRounding.ToPositiveInfinity));
    }

    private static async ValueTask GenerateHashAsync(string hypotheticalDuplicate, IHashGenerator hashGenerator,
        decimal percentageOfFileToHash, ConcurrentDictionary<byte[], ConcurrentStack<File>> partialDuplicates,
        IHubContext<NotificationHub> notificationContext, Memory<int> progressReference,
        ChannelWriter<int> progressWriter, CancellationToken cancellationToken)
    {
        try
        {
            var length = 0L;
            var hash = await hashGenerator.GenerateHashAsync(hypotheticalDuplicate,
                fileSize =>
                {
                    length = fileSize;
                    return GetLengthToHash(fileSize, percentageOfFileToHash);
                }, cancellationToken);

            var file = new File
            {
                Path = hypotheticalDuplicate,
                DateModified = System.IO.File.GetLastWriteTime(hypotheticalDuplicate),
                Size = length,
                Hash = hash
            };

            var group = partialDuplicates.GetOrAdd(file.Hash, _ => new ConcurrentStack<File>());
            group.Push(file);

            var current = Interlocked.Increment(ref MemoryMarshal.GetReference(progressReference.Span));

            if (current % 100 == 0)
                await progressWriter.WriteAsync(current, cancellationToken);
        }
        catch (IOException)
        {
            await SendError($"File {hypotheticalDuplicate} is already being used by another application",
                notificationContext, cancellationToken);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    private static Task SendError(string message, IHubContext<NotificationHub> notificationContext,
        CancellationToken cancellationToken = default)
    {
        return notificationContext.Clients.All.SendAsync("notify",
            new Notification(NotificationType.Exception, message), cancellationToken);
    }

    private async Task SendProgress(ChannelReader<int> progressReader, decimal percentageOfFileToHash,
        CancellationToken cancellationToken)
    {
        var progress = 0;
        await foreach (var hashProcessed in progressReader.ReadAllAsync(cancellationToken))
        {
            progress = hashProcessed;

            await notificationContext.Clients.All.SendAsync("notify",
                new Notification(percentageOfFileToHash switch
                {
                    0.1m => NotificationType.HashGenerationProgress,
                    _ => NotificationType.TotalProgress
                }, progress.ToString()), cancellationToken);
        }

        await notificationContext.Clients.All.SendAsync("notify",
            new Notification(percentageOfFileToHash switch
            {
                0.1m => NotificationType.HashGenerationProgress,
                _ => NotificationType.TotalProgress
            }, progress.ToString()), cancellationToken);
    }
}