using System.Collections.Concurrent;
using System.Text;
using API.Implementations.Common;
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

    public async Task<IEnumerable<IGrouping<byte[], File>>> FindDuplicateByHash(
        List<string> hypotheticalDuplicates,
        CancellationToken token)
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;

        var duplicates =
            new ConcurrentDictionary<byte[], ConcurrentQueue<File>>();
        // Get duplicates after hashing a tenth of each file
        await await SetPartialDuplicates(hypotheticalDuplicates, duplicates, 10, token);

       hypotheticalDuplicates = duplicates.Where(group => group.Value.Count > 1).SelectMany(group => group.Value.Select(file => file.Path)).ToList();

       duplicates.Clear();
       
        // Get duplicates after hashing each full file
        await await SetPartialDuplicates(hypotheticalDuplicates, duplicates, 1, token);

        return duplicates.Where(group => group.Value.Count > 1).SelectMany(group =>
                group.Value.OrderByDescending(file => file.DateModified)
                    .Select(files => new { group.Key, Value = files }))
            .ToLookup(group => group.Key, group => group.Value);
    }

    private Task<Task> SetPartialDuplicates(List<string> hypotheticalDuplicates,
        ConcurrentDictionary<byte[], ConcurrentQueue<File>> partialDuplicates, long lengthDivisor,
        CancellationToken cancellationToken)
    {
        return Task.Factory.StartNew(async () =>
        {
            var start = 0;
            var hashGenerationProgress = 0;

            while (start != hypotheticalDuplicates.Count)
            {
                var count = 2500;
                if (start + count > hypotheticalDuplicates.Count)
                    count = hypotheticalDuplicates.Count - start;

                await Parallel.ForEachAsync(hypotheticalDuplicates.GetRange(start, count),
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

                        if (hash.Length == 0)
                        {
                            await _notificationContext.Clients.All.SendAsync("Notify",
                                new Notification(NotificationType.Exception,
                                    $"File {file.Path} is corrupted"), similarityToken);
                            return;
                        }

                        partialDuplicates.AddOrUpdate(hash, new ConcurrentQueue<File>([file]), (_, duplicates) =>
                        {
                            duplicates.Enqueue(file);
                            return duplicates;
                        });

                        Interlocked.Increment(ref hashGenerationProgress);
                        var current = Interlocked.CompareExchange(ref hashGenerationProgress, 0, 0);
                        await _notificationContext.Clients.All.SendAsync("Notify",
                            new Notification(lengthDivisor switch
                                {
                                    10 => NotificationType.HashGenerationProgress,
                                    _ => NotificationType.TotalProgress
                                },
                                current.ToString()),
                            similarityToken);
                    });

                start += count;
                GC.Collect();
            }
        }, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
}