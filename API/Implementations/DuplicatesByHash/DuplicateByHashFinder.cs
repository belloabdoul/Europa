using System.Collections.Concurrent;
using System.Threading.Channels;
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

    public async Task<IEnumerable<IGrouping<string, File>>> FindDuplicateByHash(HashSet<string> hypotheticalDuplicates,
        CancellationToken cancellationToken)
    {
        var progressChannel = Channel.CreateBounded<Notification>(new BoundedChannelOptions(1)
            { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.DropNewest });

        ConcurrentDictionary<string, ConcurrentQueue<File>> duplicates;

        // Get duplicates after hashing a tenth of each file
        using (var progressTask = SendProgress(progressChannel.Reader, cancellationToken))
        {
            var files = hypotheticalDuplicates;
            var progressChannelWriter = progressChannel.Writer;
            using (var hashGenerationTask = Task.Factory.StartNew(
                       () => SetPartialDuplicates(files, 10, progressChannelWriter, cancellationToken),
                       cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default))
            {
                await Task.WhenAll(await hashGenerationTask, progressTask);

                duplicates = hashGenerationTask.Result.Result;

                hypotheticalDuplicates = duplicates.Where(group => group.Value.Count > 1)
                    .SelectMany(group => group.Value.Select(file => file.Path)).ToHashSet();

                duplicates.Clear();
            }
        }

        // Get duplicates after hashing each full file
        progressChannel = Channel.CreateBounded<Notification>(new BoundedChannelOptions(1)
            { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.DropNewest });

        using (var progressTask = SendProgress(progressChannel.Reader, cancellationToken))
        using (var hashGenerationTask = Task.Factory.StartNew(() =>
                       SetPartialDuplicates(hypotheticalDuplicates, 1, progressChannel.Writer, cancellationToken),
                   cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default))

        {
            await Task.WhenAll(await hashGenerationTask, progressTask);

            duplicates = hashGenerationTask.Result.Result;
        }

        return duplicates.Where(group => group.Value.Count > 1).SelectMany(group =>
                group.Value.OrderByDescending(file => file.DateModified)
                    .Select(files => new { group.Key, Value = files }))
            .ToLookup(group => group.Key, group => group.Value);
    }

    private async Task<ConcurrentDictionary<string, ConcurrentQueue<File>>> SetPartialDuplicates(
        HashSet<string> hypotheticalDuplicates, long lengthDivisor, ChannelWriter<Notification> progressChannelWriter,
        CancellationToken cancellationToken)
    {
        var hashGenerationProgress = 0;
        var partialDuplicates =
            new ConcurrentDictionary<string, ConcurrentQueue<File>>(Environment.ProcessorCount,
                hypotheticalDuplicates.Count);

        await Parallel.ForEachAsync(hypotheticalDuplicates,
            new ParallelOptions { CancellationToken = cancellationToken },
            async (hypotheticalDuplicate, similarityToken) =>
            {
                try
                {
                    using var fileHandle = _fileReader.GetFileHandle(hypotheticalDuplicate);

                    var size = RandomAccess.GetLength(fileHandle);

                    var bytesToHash = size / lengthDivisor;

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
                        Size = RandomAccess.GetLength(fileHandle),
                        Hash = hash,
                    };

                    partialDuplicates.TryAdd(file.Hash, new ConcurrentQueue<File>());

                    partialDuplicates[file.Hash].Enqueue(file);

                    var current = Interlocked.Increment(ref hashGenerationProgress);

                    await progressChannelWriter.WriteAsync(new Notification(lengthDivisor switch
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

        progressChannelWriter.Complete();

        return partialDuplicates;
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