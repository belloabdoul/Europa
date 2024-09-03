using System.Collections.Concurrent;
using System.Threading.Channels;
using Api.Implementations.Common;
using Blake3;
using Core.Entities;
using Core.Interfaces;
using Microsoft.AspNetCore.SignalR;
using File = Core.Entities.File;

namespace Api.Implementations.DuplicatesByHash;

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

    public async Task<IEnumerable<IGrouping<Hash, File>>> FindSimilarFilesAsync(
        string[] hypotheticalDuplicates, CancellationToken cancellationToken = default)
    {
        // Partial hash generation
        using var partialHashGenerationTask =
            SetPartialDuplicates(hypotheticalDuplicates, 10, cancellationToken);

        if (partialHashGenerationTask.IsCanceled)
            return [];

        var duplicates = partialHashGenerationTask.GetAwaiter().GetResult();

        hypotheticalDuplicates = duplicates.Where(group => group.Value.Count > 1)
            .SelectMany(group => group.Value.Select(file => file.Path)).ToArray();

        duplicates.Clear();

        // Full hash generation

        using var fullHashGenerationTask =
            SetPartialDuplicates(hypotheticalDuplicates, 1, cancellationToken);

        await fullHashGenerationTask;

        if (fullHashGenerationTask.IsCanceled)
            return [];

        duplicates = fullHashGenerationTask.GetAwaiter().GetResult();

        return duplicates.Where(group => group.Value.Count > 1).SelectMany(group =>
                group.Value.OrderByDescending(file => file.DateModified)
                    .Select(files => new { group.Key, Value = files }))
            .ToLookup(group => group.Key, group => group.Value);
    }

    private async Task<ConcurrentDictionary<Hash, ConcurrentQueue<File>>> SetPartialDuplicates(
        string[] hypotheticalDuplicates, long lengthDivisor, CancellationToken cancellationToken)
    {
        var progress = 0;

        var partialDuplicates =
            new ConcurrentDictionary<Hash, ConcurrentQueue<File>>(Environment.ProcessorCount,
                hypotheticalDuplicates.Length);

        var progressChannel = Channel.CreateBounded<int>(new BoundedChannelOptions(1)
            { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.DropNewest });

        using var hashingTask = Parallel.ForEachAsync(hypotheticalDuplicates,
            new ParallelOptions
            {
                CancellationToken = cancellationToken, MaxDegreeOfParallelism = Environment.ProcessorCount
            }, async (hypotheticalDuplicate, hashingToken) =>
            {
                if (await GenerateHashAsync(
                        hypotheticalDuplicate, lengthDivisor, partialDuplicates,
                        _hashGenerator, _notificationContext, hashingToken))
                    await progressChannel.Writer.WriteAsync(Interlocked.Increment(ref progress), cancellationToken);
            }).ContinueWith(
            _ => { progressChannel.Writer.Complete(); }, cancellationToken,
            TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);

        await foreach (var hashProcessed in progressChannel.Reader.ReadAllAsync(cancellationToken))
        {
            await _notificationContext.Clients.All.SendAsync("notify", new Notification(lengthDivisor switch
            {
                10 => NotificationType.HashGenerationProgress,
                _ => NotificationType.TotalProgress
            }, hashProcessed.ToString()), cancellationToken);
            if (hashProcessed % 1000 == 0)
                GC.Collect(2, GCCollectionMode.Default, false, true);
        }


        await hashingTask;

        return partialDuplicates;
    }

    public static async ValueTask<bool> GenerateHashAsync(string hypotheticalDuplicate, long lengthDivisor,
        ConcurrentDictionary<Hash, ConcurrentQueue<File>> partialDuplicates, IHashGenerator hashGenerator,
        IHubContext<NotificationHub> notificationContext, CancellationToken cancellationToken)
    {
        try
        {
            using var fileHandle = FileReader.GetFileHandle(hypotheticalDuplicate, true);

            var size = RandomAccess.GetLength(fileHandle);

            var bytesToHash = Convert.ToInt64(Math.Round(decimal.Divide(size, lengthDivisor),
                MidpointRounding.ToPositiveInfinity));
            

            var hash = hashGenerator.GenerateHash(fileHandle, bytesToHash, cancellationToken);

            if (!hash.HasValue)
            {
                await SendError($"File {hypotheticalDuplicate} is corrupted", notificationContext,
                    cancellationToken);
                return false;
            }

            var file = new File
            {
                Path = hypotheticalDuplicate,
                DateModified = System.IO.File.GetLastWriteTime(fileHandle),
                Size = size,
                Hash = hash.Value
            };

            partialDuplicates.TryAdd(file.Hash, new ConcurrentQueue<File>());

            partialDuplicates[file.Hash].Enqueue(file);

            return true;
        }
        catch (IOException)
        {
            await SendError($"File {hypotheticalDuplicate} is already being used by another application",
                notificationContext, cancellationToken);
        }

        return false;
    }

    public static Task SendError(string message, IHubContext<NotificationHub> notificationContext,
        CancellationToken cancellationToken)
    {
        return notificationContext.Clients.All.SendAsync("notify",
            new Notification(NotificationType.Exception, message),
            cancellationToken);
    }
}