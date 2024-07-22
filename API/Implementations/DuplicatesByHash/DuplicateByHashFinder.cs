using System.Collections.Concurrent;
using System.Text;
using API.Implementations.Common;
using Blake3;
using Core.Entities;
using Core.Interfaces.Common;
using Core.Interfaces.DuplicatesByHash;
using Microsoft.AspNetCore.SignalR;
using File = Core.Entities.File;

namespace API.Implementations.DuplicatesByHash;

public class DuplicateByHashFinder : IDuplicateByHashFinder
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

    public async Task<IEnumerable<IGrouping<Hash, File>>> FindDuplicateByHash(
        HashSet<string> hypotheticalDuplicates,
        CancellationToken token)
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;

        // Get duplicates after hashing a tenth of each file
        var duplicates = await await SetPartialDuplicates(hypotheticalDuplicates, 10, token);

        hypotheticalDuplicates = duplicates.Where(group => group.Value.Count > 1)
            .SelectMany(group => group.Value.Select(file => file.Path)).ToHashSet();

        duplicates.Clear();

        // Get duplicates after hashing each full file
        duplicates = await await SetPartialDuplicates(hypotheticalDuplicates, 1, token);

        return duplicates.Where(group => group.Value.Count > 1).SelectMany(group =>
                group.Value.OrderByDescending(file => file.DateModified)
                    .Select(files => new { group.Key, Value = files }))
            .ToLookup(group => group.Key, group => group.Value);
    }

    private Task<Task<ConcurrentDictionary<Hash, ConcurrentQueue<File>>>> SetPartialDuplicates(HashSet<string> hypotheticalDuplicates, long lengthDivisor,
        CancellationToken cancellationToken)
    {
        return Task.Factory.StartNew(async () =>
        {
            var hashGenerationProgress = 0;
            var partialDuplicates = new ConcurrentDictionary<Hash, ConcurrentQueue<File>>();

            await Parallel.ForEachAsync(hypotheticalDuplicates,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken
                },
                async (path, similarityToken) =>
                {
                    using var fileHandle = _fileReader.GetFileHandle(path);

                    var file = new File
                    {
                        Path = path,
                        DateModified = System.IO.File.GetLastWriteTimeUtc(fileHandle),
                        Size = RandomAccess.GetLength(fileHandle),
                    };

                    var bytesToHash = file.Size / lengthDivisor;

                    var hash = await _hashGenerator.GenerateHashAsync(fileHandle, bytesToHash,
                        cancellationToken: cancellationToken);

                    if (!hash.HasValue)
                    {
                        await _notificationContext.Clients.All.SendAsync("notify",
                            new Notification(NotificationType.Exception,
                                $"File {file.Path} is corrupted"), similarityToken);
                        return;
                    }

                    partialDuplicates.AddOrUpdate(hash.Value, new ConcurrentQueue<File>([file]), (_, duplicates) =>
                    {
                        duplicates.Enqueue(file);
                        return duplicates;
                    });

                    Interlocked.Increment(ref hashGenerationProgress);
                    var current = Interlocked.CompareExchange(ref hashGenerationProgress, 0, 0);
                    await _notificationContext.Clients.All.SendAsync("notify",
                        new Notification(lengthDivisor switch
                            {
                                10 => NotificationType.HashGenerationProgress,
                                _ => NotificationType.TotalProgress
                            },
                            current.ToString()),
                        similarityToken);

                    if (current % 1000 == 0)
                        GC.Collect();
                });
            
            return partialDuplicates;
        }, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
}