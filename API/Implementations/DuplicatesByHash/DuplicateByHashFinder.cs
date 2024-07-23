using System.Collections.Concurrent;
using System.Threading.Channels;
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

    public async Task<IEnumerable<IGrouping<Hash, File>>> FindDuplicateByHash(HashSet<string> hypotheticalDuplicates,
        CancellationToken cancellationToken)
    {
        var progressChannel = Channel.CreateBounded<Notification>(new BoundedChannelOptions(1)
            { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.DropNewest, AllowSynchronousContinuations = true});

        ConcurrentDictionary<Hash, ConcurrentQueue<File>> duplicates;

        // Get duplicates after hashing a tenth of each file
        using (var progressTask = SendProgress(progressChannel.Reader, cancellationToken))
        using (var hashGenerationTask =
               SetPartialDuplicates(hypotheticalDuplicates, 10, progressChannel.Writer, cancellationToken))
        {
            await Task.WhenAll(await hashGenerationTask, progressTask);

            duplicates = hashGenerationTask.Result.Result;

            hypotheticalDuplicates = duplicates.Where(group => group.Value.Count > 1)
                .SelectMany(group => group.Value.Select(file => file.Path)).ToHashSet();

            duplicates.Clear();
        }

        // Get duplicates after hashing each full file

        progressChannel = Channel.CreateBounded<Notification>(new BoundedChannelOptions(1)
            { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.DropNewest });

        using (var progressTask = SendProgress(progressChannel.Reader, cancellationToken))
        using (var hashGenerationTask =
               SetPartialDuplicates(hypotheticalDuplicates, 1, progressChannel.Writer, cancellationToken))
        {
            await Task.WhenAll(await hashGenerationTask, progressTask);

            duplicates = hashGenerationTask.Result.Result;
        }

        return duplicates.Where(group => group.Value.Count > 1).SelectMany(group =>
                group.Value.OrderByDescending(file => file.DateModified)
                    .Select(files => new { group.Key, Value = files }))
            .ToLookup(group => group.Key, group => group.Value);
    }

    private Task<Task<ConcurrentDictionary<Hash, ConcurrentQueue<File>>>> SetPartialDuplicates(
        HashSet<string> hypotheticalDuplicates, long lengthDivisor, ChannelWriter<Notification> progressChannelWriter,
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
                        DateModified = System.IO.File.GetLastWriteTime(fileHandle),
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

                    var current = Interlocked.Increment(ref hashGenerationProgress);

                    await progressChannelWriter.WriteAsync(new Notification(lengthDivisor switch
                        {
                            10 => NotificationType.HashGenerationProgress,
                            _ => NotificationType.TotalProgress
                        },
                        current.ToString()), cancellationToken: similarityToken);

                    if (current % 1000 == 0)
                        GC.Collect();
                });
            
            progressChannelWriter.Complete();

            return partialDuplicates;
        }, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    private async Task SendProgress(ChannelReader<Notification> progressChannelReader,
        CancellationToken cancellationToken)
    {
        await foreach (var notification in progressChannelReader.ReadAllAsync(cancellationToken: cancellationToken))
        {
            await _notificationContext.Clients.All.SendAsync("notify", notification,
                cancellationToken: cancellationToken);
        }
    }
}