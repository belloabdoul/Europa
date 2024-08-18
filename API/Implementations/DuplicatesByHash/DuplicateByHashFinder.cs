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
    private const int BufferSize = 1_048_576;

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

    private async Task<ConcurrentDictionary<string, ConcurrentQueue<File>>> SetPartialDuplicates(
        string[] hypotheticalDuplicates, long lengthDivisor, CancellationToken cancellationToken)
    {
        var progress = 0;

        var partialDuplicates =
            new ConcurrentDictionary<string, ConcurrentQueue<File>>(Environment.ProcessorCount,
                hypotheticalDuplicates.Length);

        var progressChannel = Channel.CreateUnbounded<bool>(new UnboundedChannelOptions
            { SingleReader = true, SingleWriter = false });

        using var hashingTask = Parallel.ForEachAsync(hypotheticalDuplicates,
            new ParallelOptions
            {
                CancellationToken = cancellationToken, MaxDegreeOfParallelism = Environment.ProcessorCount
            }, async (hypotheticalDuplicate, hashingToken) => await progressChannel.Writer.WriteAsync(
                await GenerateHashAsync(
                    hypotheticalDuplicate, lengthDivisor, partialDuplicates,
                    _hashGenerator, _notificationContext, hashingToken), hashingToken)).ContinueWith(
            _ => { progressChannel.Writer.Complete(); }, cancellationToken,
            TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);

        await foreach (var hashProcessed in progressChannel.Reader.ReadAllAsync(cancellationToken))
        {
            if (!hashProcessed)
                continue;

            await _notificationContext.Clients.All.SendAsync("notify", new Notification(lengthDivisor switch
            {
                10 => NotificationType.HashGenerationProgress,
                _ => NotificationType.TotalProgress
            }, (++progress).ToString()), cancellationToken);
            if (progress % 1000 == 0)
                GC.Collect();
        }

        await hashingTask;

        return partialDuplicates;
    }

    public static async ValueTask<bool> GenerateHashAsync(string hypotheticalDuplicate, long lengthDivisor,
        ConcurrentDictionary<string, ConcurrentQueue<File>> partialDuplicates, IHashGenerator hashGenerator,
        IHubContext<NotificationHub> notificationContext, CancellationToken cancellationToken)
    {
        var pointer = IntPtr.Zero;

        try
        {
            using var fileHandle = FileReader.GetFileHandle(hypotheticalDuplicate, false, true);

            var size = RandomAccess.GetLength(fileHandle);

            var bytesToHash = Convert.ToInt64(Math.Round(decimal.Divide(size, lengthDivisor),
                MidpointRounding.ToPositiveInfinity));

            Memory<byte> buffer;
            unsafe
            {
                pointer = new IntPtr(FileReader.AllocateAlignedMemory(BufferSize));
                buffer = FileReader.AsMemory(pointer, BufferSize);
            }

            var hash = await hashGenerator.GenerateHashAsync(fileHandle, bytesToHash, buffer, cancellationToken);

            if (string.IsNullOrEmpty(hash))
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
                Hash = hash
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
        finally
        {
            if (pointer != IntPtr.Zero)
                FileReader.FreeAlignedMemory(pointer);
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