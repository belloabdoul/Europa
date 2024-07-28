using System.Collections.Concurrent;
using System.Threading.Channels;
using API.Implementations.Common;
using Core.Entities;
using Core.Entities.Redis;
using Core.Interfaces.Common;
using Core.Interfaces.DuplicatesByHash;
using Core.Interfaces.SimilarImages;
using Database.Interfaces;
using Microsoft.AspNetCore.SignalR;
using Redis.OM;
using File = Core.Entities.File;

namespace API.Implementations.SimilarImages;

public class SimilarImageFinder : ISimilarImagesFinder
{
    private readonly IFileReader _fileReader;
    private readonly IFileTypeIdentifier _fileTypeIdentifier;
    private readonly IHashGenerator _hashGenerator;

    private readonly IDbHelpers _dbHelpers;
    private readonly IImageHash _imageHashGenerator;
    private readonly IHubContext<NotificationHub> _notificationContext;

    public SimilarImageFinder(IHubContext<NotificationHub> notificationContext, IFileReader fileReader,
        IFileTypeIdentifier fileTypeIdentifier, IHashGenerator hashGenerator,
        IImageHash imageHashGenerator, IDbHelpers dbHelpers)
    {
        _notificationContext = notificationContext;
        _fileReader = fileReader;
        _fileTypeIdentifier = fileTypeIdentifier;
        _hashGenerator = hashGenerator;
        _imageHashGenerator = imageHashGenerator;
        _dbHelpers = dbHelpers;
    }

    public async Task<IEnumerable<IGrouping<string, File>>> FindSimilarImagesAsync(
        HashSet<string> hypotheticalDuplicates, double degreeOfSimilarity,
        CancellationToken cancellationToken)
    {
        // Part 1 : Generate and cache perceptual hash of non-corrupted files
        var progress = Channel.CreateBounded<Notification>(new BoundedChannelOptions(1)
            { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.DropNewest });

        var progressTask = SendProgress(progress.Reader, cancellationToken);

        var progressWriter = progress.Writer;

        var hashGenerationTask = Task.Factory.StartNew(() => GenerateImageHashForNonCorruptedFiles(
                hypotheticalDuplicates, progressWriter, cancellationToken), cancellationToken,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);

        await Task.WhenAll(await hashGenerationTask, progressTask);

        if (progressTask.IsCanceled || hashGenerationTask.IsCanceled)
            return [];

        var imagesGroups = hashGenerationTask.Result.Result;

        // Part 2 : Group similar images together
        progress = Channel.CreateBounded<Notification>(new BoundedChannelOptions(1)
            { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.DropNewest });

        var groupingChannel = Channel.CreateUnbounded<ImagesGroup>();

        var finalImages = new ConcurrentQueue<File>();

        progressTask = SendProgress(progress, cancellationToken);

        var groupingTask = Task.Factory.StartNew(() => ProcessGroupsForFinalList(groupingChannel, imagesGroups,
                finalImages, cancellationToken: cancellationToken), cancellationToken: cancellationToken,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);

        var similarityTask = Task.Factory.StartNew(
            () => LinkSimilarImagesGroupsToOneAnother(imagesGroups, degreeOfSimilarity,
                groupingChannel, progress.Writer, cancellationToken), cancellationToken: cancellationToken,
            creationOptions: TaskCreationOptions.LongRunning, scheduler: TaskScheduler.Default);

        await Task.WhenAll(await similarityTask, progressTask, await groupingTask);

        Console.WriteLine(imagesGroups.Count);
        var groups = finalImages.GroupBy(file => file.Hash)
            .Where(i => i.Count() != 1).ToList();
        return groups;
    }

    private async Task<Dictionary<string, ImagesGroup>> GenerateImageHashForNonCorruptedFiles(
        HashSet<string> hypotheticalDuplicates, ChannelWriter<Notification> progressWriter,
        CancellationToken cancellationToken)
    {
        var progress = 0;
        var copiesGroups =
            new ConcurrentDictionary<string, ImagesGroup>(Environment.ProcessorCount, hypotheticalDuplicates.Count);

        // Generate integrity hash and group perfect copies together
        await Parallel.ForEachAsync(hypotheticalDuplicates,
            new ParallelOptions { CancellationToken = cancellationToken },
            async (hypotheticalDuplicate, hashingToken) =>
            {
                try
                {
                    var type = _fileTypeIdentifier.GetFileType(hypotheticalDuplicate);

                    switch (type)
                    {
                        case FileType.CorruptUnknownOrUnsupported:
                            await _notificationContext.Clients.All.SendAsync("notify",
                                new Notification(NotificationType.Exception,
                                    $"File {hypotheticalDuplicates} is either corrupted, unknown or unsupported"),
                                cancellationToken: hashingToken);
                            break;
                        case FileType.Image:
                        {
                            using var fileHandle = _fileReader.GetFileHandle(hypotheticalDuplicate);

                            var length = RandomAccess.GetLength(fileHandle);

                            var hash = await _hashGenerator.GenerateHashAsync(fileHandle, length,
                                cancellationToken: hashingToken);

                            if (string.IsNullOrEmpty(hash))
                            {
                                await _notificationContext.Clients.All.SendAsync("notify",
                                    new Notification(NotificationType.Exception,
                                        $"File {hypotheticalDuplicate} is either of type unknown, corrupted or unsupported"),
                                    cancellationToken: hashingToken);
                                return;
                            }

                            var isFirst = copiesGroups.TryAdd(hash, new ImagesGroup());
                            var group = copiesGroups[hash];

                            if (isFirst)
                            {
                                group.Id = hash;
                                group.Size = length;
                                group.DateModified = System.IO.File.GetLastWriteTime(hypotheticalDuplicate);

                                var imageHash = await
                                    _dbHelpers.GetImageInfosAsync(group.Id);

                                if (imageHash == null)
                                {
                                    try
                                    {
                                        group.ImageHash =
                                            Vector.Of(_imageHashGenerator.GenerateHash(hypotheticalDuplicate));

                                        group.ImageHash.Embed(
                                            new ByteToFloatVectorizerAttribute(group.ImageHash.Value.Length));

                                        await _dbHelpers.CacheHashAsync(group);

                                        group.IsCorruptedOrUnsupported = false;
                                    }
                                    catch (Exception)
                                    {
                                        group.IsCorruptedOrUnsupported = true;
                                    }
                                }
                                else
                                {
                                    group.ImageHash = imageHash;
                                    group.IsCorruptedOrUnsupported = false;
                                }

                                if (!group.IsCorruptedOrUnsupported)
                                {
                                    var current = Interlocked.Increment(ref progress);

                                    await progressWriter.WriteAsync(
                                        new Notification(NotificationType.HashGenerationProgress,
                                            progress.ToString()),
                                        cancellationToken: hashingToken);

                                    if (current % 1000 == 0)
                                        GC.Collect();
                                }
                            }

                            group.Duplicates.Enqueue(hypotheticalDuplicate);
                            break;
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    throw;
                }
            });

        progressWriter.Complete();

        return await copiesGroups.ToAsyncEnumerable().WhereAwaitWithCancellation(
            async (group, corruptionToken) =>
            {
                if (!group.Value.IsCorruptedOrUnsupported)
                    return true;
                for (var i = 0; i < group.Value.Duplicates.Count; i++)
                {
                    group.Value.Duplicates.TryDequeue(out var duplicate);
                    await _notificationContext.Clients.All.SendAsync("notify",
                        new Notification(NotificationType.Exception,
                            $"File {duplicate} is either of type unknown, corrupted or unsupported"),
                        cancellationToken: corruptionToken);
                }

                return false;
            }).ToDictionaryAsync(group => group.Key, group => group.Value, cancellationToken);
    }

    private async Task SendProgress(ChannelReader<Notification> progressReader, CancellationToken cancellationToken)
    {
        await foreach (var progress in progressReader.ReadAllAsync(
                           cancellationToken: cancellationToken))
        {
            await _notificationContext.Clients.All.SendAsync("notify", progress, cancellationToken: cancellationToken);
        }
    }

    private async Task LinkSimilarImagesGroupsToOneAnother(Dictionary<string, ImagesGroup> imagesGroups,
        double degreeOfSimilarity, ChannelWriter<ImagesGroup> groupingChannelWriter,
        ChannelWriter<Notification> progressWriter, CancellationToken cancellationToken)
    {
        var progress = 0;

        await Parallel.ForEachAsync(imagesGroups.Keys, new ParallelOptions { CancellationToken = cancellationToken },
            async (key, similarityToken) =>
            {
                var group = imagesGroups[key];

                group.SimilarImages = await _dbHelpers.GetSimilarImagesAlreadyDoneInRange(group.Id);

                var newSimilarities = await _dbHelpers.GetSimilarImages(
                    group.Id,
                    group.ImageHash!,
                    degreeOfSimilarity,
                    group.SimilarImages);

                var isEmpty = group.SimilarImages.Count == 0;

                foreach (var similarity in newSimilarities)
                {
                    group.SimilarImages.Add(similarity.DuplicateId);
                }

                if (newSimilarities.Count > 0)
                    await _dbHelpers.LinkToSimilariImagesAsync(group.Id, newSimilarities, isEmpty);

                var current = Interlocked.Increment(ref progress);

                await progressWriter.WriteAsync(
                    new Notification(NotificationType.SimilaritySearchProgress, progress.ToString()),
                    cancellationToken: similarityToken);

                // The current group is sent for grouping if it has at least another similar image or there are  m
                // at least 2 copies of the same image
                if (group.SimilarImages.Count > 1 || group.Duplicates.Count > 1)
                    await groupingChannelWriter.WriteAsync(imagesGroups[key],
                        cancellationToken: similarityToken);
                else
                    imagesGroups.Remove(group.Id, out _);

                if (current % 1000 == 0)
                    GC.Collect();
            });

        progressWriter.Complete();
        groupingChannelWriter.Complete();
    }

    private async Task ProcessGroupsForFinalList(Channel<ImagesGroup, ImagesGroup> groupingChannel,
        Dictionary<string, ImagesGroup> imagesGroups, ConcurrentQueue<File> finalImages,
        CancellationToken cancellationToken)
    {
        var progress = 0;

        var notificationCeiling = decimal.Divide(imagesGroups.Count, 400);

        var groupsDone = new HashSet<string>();

        try
        {
            await foreach (var currentImagesGroup in groupingChannel.Reader.ReadAllAsync(
                               cancellationToken: cancellationToken))
            {
                if (groupsDone.Contains(currentImagesGroup.Id))
                {
                    currentImagesGroup.SimilarImages.ExceptWith(groupsDone);

                    if (currentImagesGroup.SimilarImages.Count == 0)
                        imagesGroups.Remove(currentImagesGroup.Id, out _);
                }
                else
                {
                    switch (currentImagesGroup.SimilarImages.Count)
                    {
                        case > 1:
                        case 1 when currentImagesGroup.Duplicates.Count > 1:
                            LinkImagesToParentGroup(currentImagesGroup, groupsDone, imagesGroups, finalImages,
                                cancellationToken);

                            foreach (var similarImageGroup in currentImagesGroup.SimilarImages)
                            {
                                groupsDone.Add(similarImageGroup);
                            }

                            imagesGroups.Remove(currentImagesGroup.Id, out _);
                            await _notificationContext.Clients.All.SendAsync("notify",
                                new Notification(NotificationType.TotalProgress,
                                    (++progress).ToString()), cancellationToken: cancellationToken);
                            break;
                        default:
                            imagesGroups.Remove(currentImagesGroup.Id, out _);
                            break;
                    }
                }

                if (decimal.Remainder(progress, notificationCeiling) == 0)
                    GC.Collect();
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    private void LinkImagesToParentGroup(ImagesGroup parentGroup, HashSet<string> groupsDone,
        Dictionary<string, ImagesGroup> imagesGroups, ConcurrentQueue<File> finalImages, CancellationToken token)
    {
        Parallel.ForEach(parentGroup.SimilarImages, new ParallelOptions { CancellationToken = token },
            similarImagesGroup =>
            {
                if (similarImagesGroup == parentGroup.Id)
                {
                    while (!parentGroup.Duplicates.IsEmpty)
                    {
                        if (parentGroup.Duplicates.TryDequeue(out var image))
                            finalImages.Enqueue(new File
                            {
                                Path = image,
                                Size = parentGroup.Size,
                                DateModified = parentGroup.DateModified,
                                Hash = parentGroup.Id
                            });
                    }
                }
                else
                {
                    if (!imagesGroups.TryGetValue(similarImagesGroup, out var result))
                        return;
                    foreach (var image in result.Duplicates)
                    {
                        finalImages.Enqueue(new File
                        {
                            Path = image,
                            Size = result.Size,
                            DateModified = result.DateModified,
                            Hash = parentGroup.Id
                        });
                    }

                    // if (groupsDone.Contains(result.Id))
                    // {
                    //     result.SimilarImagesGroups.ExceptWith(parentGroup.SimilarImagesGroups);
                    //     if (result.SimilarImagesGroups.Count == 0)
                    //         imagesGroups.Remove(similarImagesGroup);
                    // }
                }
            });
    }
}