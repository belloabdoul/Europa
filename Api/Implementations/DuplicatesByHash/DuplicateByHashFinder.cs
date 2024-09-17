using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Api.Implementations.Common;
using CommunityToolkit.HighPerformance.Buffers;
using Core.Entities;
using Core.Interfaces;
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

        var progressChannel = Channel.CreateUnboundedPrioritized(new UnboundedPrioritizedChannelOptions<int>
            { SingleWriter = false, SingleReader = true });

        try
        {
            await Task.WhenAll(SendProgress(progressChannel.Reader, 0.1m, cancellationToken),
                SetPartialDuplicates(hypotheticalDuplicates, partialDuplicates, 0.1m, progressChannel.Writer,
                    cancellationToken));
        }
        catch (OperationCanceledException)
        {
            StringPool.Shared.Reset();
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
                SetPartialDuplicates(hypotheticalDuplicates, partialDuplicates, 1, progressChannel.Writer,
                    cancellationToken));
        }
        catch (OperationCanceledException)
        {
            StringPool.Shared.Reset();
            partialDuplicates.Clear();
            return [];
        }

        return partialDuplicates.Where(group => group.Value.Count > 1).SelectMany(group =>
                group.Value.OrderByDescending(file => file.DateModified)
                    .Select(files => new { group.Key, Value = files }))
            .ToLookup(group => group.Key, group => group.Value);
    }

    private async Task SetPartialDuplicates(string[] hypotheticalDuplicates,
        ConcurrentDictionary<string, ConcurrentStack<File>> partialDuplicates, decimal percentageOfFileToHash,
        ChannelWriter<int> progressWriter, CancellationToken cancellationToken)
    {
        StringPool.Shared.Reset();

        var progress = 0;

        await Parallel.ForAsync(0, hypotheticalDuplicates.Length,
            new ParallelOptions
                { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken },
            async (i, hashingToken) =>
            {
                try
                {
                    using var fileHandle = FileReader.GetFileHandle(hypotheticalDuplicates[i], true,
                        RuntimeInformation.IsOSPlatform(OSPlatform.Windows));

                    var size = RandomAccess.GetLength(fileHandle);

                    var bytesToHash = Convert.ToInt64(decimal.Round(decimal.Multiply(size, percentageOfFileToHash),
                        MidpointRounding.ToPositiveInfinity));

                    var hash = await _hashGenerator.GenerateHashAsync(fileHandle, bytesToHash, hashingToken);

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

                    var current = Interlocked.Increment(ref progress);

                    await progressWriter.WriteAsync(current, hashingToken);
                }
                catch (IOException)
                {
                    await SendError($"File {hypotheticalDuplicates[i]} is already being used by another application",
                        _notificationContext, hashingToken);
                }
            });

        progressWriter.Complete();
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
            var isNextAvailable = await progressReader.WaitToReadAsync(cancellationToken);

            if (isNextAvailable && hashProcessed % 100 != 0)
                continue;

            await _notificationContext.Clients.All.SendAsync("notify",
                new Notification(percentageOfFileToHash switch
                {
                    0.1m => NotificationType.HashGenerationProgress,
                    _ => NotificationType.TotalProgress
                }, hashProcessed.ToString()), cancellationToken);
        }
    }
}