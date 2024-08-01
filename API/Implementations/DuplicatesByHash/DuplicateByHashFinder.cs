using System.Collections.Concurrent;
using API.Implementations.Common;
using Core.Entities;
using Core.Interfaces;
using Core.Interfaces.Common;
using Microsoft.AspNetCore.SignalR;
using File = Core.Entities.File;

namespace API.Implementations.DuplicatesByHash;

public class DuplicateByHashFinder : ISimilarFilesFinder
{
    private readonly IFileReader _fileReader;
    private readonly IHashGenerator _hashGenerator;
    private readonly IHubContext<NotificationHub> _notificationContext;

    public DuplicateByHashFinder(IFileReader fileReader, IHashGenerator hashGenerator,
        IHubContext<NotificationHub> notificationContext)
    {
        _fileReader = fileReader;
        _hashGenerator = hashGenerator;
        _notificationContext = notificationContext;
    }

    public async Task<IEnumerable<IGrouping<string, File>>> FindSimilarFilesAsync(
        HashSet<string> hypotheticalDuplicates, CancellationToken cancellationToken = default)
    {
        using var partialHashGenerationTask = SetPartialDuplicates(hypotheticalDuplicates, 10, cancellationToken);

        await partialHashGenerationTask;

        if (partialHashGenerationTask.IsCanceled)
            return [];

        var duplicates = partialHashGenerationTask.GetAwaiter().GetResult();

        hypotheticalDuplicates = duplicates.Where(group => group.Value.Count > 1)
            .SelectMany(group => group.Value.Select(file => file.Path)).ToHashSet();

        duplicates.Clear();

        using var fullHashGenerationTask = SetPartialDuplicates(hypotheticalDuplicates, 1, cancellationToken);
        await fullHashGenerationTask;

        if (fullHashGenerationTask.IsCanceled)
            return [];

        duplicates = fullHashGenerationTask.GetAwaiter().GetResult();

        return duplicates.Where(group => group.Value.Count > 1).SelectMany(group =>
                group.Value.OrderByDescending(file => file.DateModified)
                    .Select(files => new { group.Key, Value = files }))
            .ToLookup(group => group.Key, group => group.Value);
    }

    private async Task<ConcurrentDictionary<string, ConcurrentQueue<File>>> SetPartialDuplicates(
        HashSet<string> hypotheticalDuplicates, long lengthDivisor, CancellationToken cancellationToken)
    {
        var progress = 0;
        var partialDuplicates =
            new ConcurrentDictionary<string, ConcurrentQueue<File>>(Environment.ProcessorCount,
                hypotheticalDuplicates.Count);

        await Parallel.ForEachAsync(hypotheticalDuplicates,
            new ParallelOptions { CancellationToken = cancellationToken },
            async (hypotheticalDuplicate, similarityToken) =>
            {
                try
                {
                    using var fileHandle =
                        _fileReader.GetFileHandle(hypotheticalDuplicate, isAsync: false);

                    var size = RandomAccess.GetLength(fileHandle);

                    var bytesToHash = Convert.ToInt64(Math.Round(decimal.Divide(size, lengthDivisor),
                        MidpointRounding.ToPositiveInfinity));

                    var hash = await _hashGenerator.GenerateHashAsync(fileHandle, bytesToHash,
                        cancellationToken: cancellationToken);

                    if (string.IsNullOrEmpty(hash))
                    {
                        await _notificationContext.Clients.All.SendAsync("notify",
                            new Notification(NotificationType.Exception,
                                $"File {hypotheticalDuplicate} is corrupted"), similarityToken);
                        return;
                    }

                    var file = new File
                    {
                        Path = hypotheticalDuplicate,
                        DateModified = System.IO.File.GetLastWriteTime(fileHandle),
                        Size = size,
                        Hash = hash,
                    };

                    partialDuplicates.TryAdd(file.Hash, new ConcurrentQueue<File>());

                    partialDuplicates[file.Hash].Enqueue(file);

                    var current = Interlocked.Increment(ref progress);

                    await _notificationContext.Clients.All.SendAsync("notify", new Notification(lengthDivisor switch
                        {
                            10 => NotificationType.HashGenerationProgress,
                            _ => NotificationType.TotalProgress
                        },
                        current.ToString()), cancellationToken: similarityToken);

                    if (current % 1000 == 0)
                        GC.Collect();
                }
                catch (IOException)
                {
                    await _notificationContext.Clients.All.SendAsync("notify", new Notification(
                            NotificationType.Exception,
                            $"File {hypotheticalDuplicate} is already being used by another application"),
                        similarityToken);
                }
            });

        return partialDuplicates;
    }
}