using System.Collections.Concurrent;
using System.Collections.Specialized;
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
        using var hashGenerationTask = GenerateImageHashForNonCorruptedFiles(hypotheticalDuplicates, cancellationToken);
        await hashGenerationTask;

        if (hashGenerationTask.IsCanceled)
            return [];

        var imagesGroups = hashGenerationTask.GetAwaiter().GetResult();

        // Part 2 : Group similar images together
        var groupingChannel = Channel.CreateUnbounded<string>();

        var finalImages = new ConcurrentQueue<File>();

        using var groupingTask = ProcessGroupsForFinalList(groupingChannel.Reader, imagesGroups, finalImages,
            cancellationToken: cancellationToken);

        using var similarityTask = LinkSimilarImagesGroupsToOneAnother(imagesGroups, degreeOfSimilarity,
            groupingChannel.Writer, cancellationToken: cancellationToken);

        await Task.WhenAll(similarityTask, groupingTask);

        Console.WriteLine(imagesGroups.Count);
        var groups = finalImages.GroupBy(file => file.Hash)
            .Where(i => i.Count() != 1).ToList();
        return groups;
    }

    private async Task<Dictionary<string, ImagesGroup>> GenerateImageHashForNonCorruptedFiles(
        HashSet<string> hypotheticalDuplicates, CancellationToken cancellationToken)
    {
        var progress = 0;

        var copiesGroups =
            new ConcurrentDictionary<string, ImagesGroup>(Environment.ProcessorCount, hypotheticalDuplicates.Count);

        // Generate integrity hash and group perfect copies together
        await Parallel.ForEachAsync(hypotheticalDuplicates,
            new ParallelOptions { CancellationToken = cancellationToken },
            async (hypotheticalDuplicate, hashingToken) =>
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

                            var imageHash = await _dbHelpers.GetImageInfosAsync(group.Id);

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

                                await _notificationContext.Clients.All.SendAsync("notify",
                                    new Notification(NotificationType.HashGenerationProgress, progress.ToString()),
                                    cancellationToken: hashingToken);

                                if (current % 1000 == 0)
                                    GC.Collect();
                            }
                        }

                        group.Duplicates.Enqueue(hypotheticalDuplicate);
                        break;
                    }
                }
            });

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

    private async Task LinkSimilarImagesGroupsToOneAnother(Dictionary<string, ImagesGroup> imagesGroups,
        double degreeOfSimilarity, ChannelWriter<string> groupingChannelWriter,
        CancellationToken cancellationToken)
    {
        var progress = 0;

        var keys = imagesGroups.Keys.ToArray();

        await Parallel.ForEachAsync(keys, new ParallelOptions { CancellationToken = cancellationToken },
            async (key, similarityToken) =>
            {
                var group = imagesGroups[key];

                group.SimilarImages = await _dbHelpers.GetSimilarImagesAlreadyDoneInRange(group.Id);

                group.SimilarImages.CollectionChanged += (sender, args) =>
                {
                    if (args.Action == NotifyCollectionChangedAction.Replace && group.SimilarImages.Count == 0)
                        imagesGroups.Remove(group.Id, out _);
                };

                group.Similarities = await _dbHelpers.GetSimilarImages(group.Id, group.ImageHash, degreeOfSimilarity,
                    group.SimilarImages);

                var isEmpty = group.SimilarImages.Count == 0;

                foreach (var similarity in group.Similarities)
                    group.SimilarImages.Add(similarity.DuplicateId);

                if (group.Similarities.Count > 0)
                    await _dbHelpers.LinkToSimilarImagesAsync(group.Id, group.Similarities, isEmpty);

                var current = Interlocked.Increment(ref progress);

                await _notificationContext.Clients.All.SendAsync("notify",
                    new Notification(NotificationType.SimilaritySearchProgress, progress.ToString()),
                    cancellationToken: similarityToken);

                // The current group is sent for grouping if it has at least another similar image or there are  m
                // at least 2 copies of the same image
                await groupingChannelWriter.WriteAsync(key, cancellationToken: similarityToken);

                if (current % 1000 == 0)
                    GC.Collect();
            });

        groupingChannelWriter.Complete();
    }

    private async Task ProcessGroupsForFinalList(ChannelReader<string> groupingChannelReader,
        Dictionary<string, ImagesGroup> imagesGroups, ConcurrentQueue<File> finalImages,
        CancellationToken cancellationToken)
    {
        var progress = 0;

        var notificationCeiling = decimal.Divide(imagesGroups.Count, 400);

        var groupsDone = new HashSet<string>();


        await foreach (var groupId in groupingChannelReader.ReadAllAsync(
                           cancellationToken: cancellationToken))
        {
            // This image's similar images have already been partially or fully processed
            // If fully processed this command will trigger the removal of the group
            // If partially processed the group will stay there until the remaining similar
            // images it is associated to trigger their removal
            switch (imagesGroups[groupId].SimilarImages.Count)
            {
                case > 1:
                case 1 when imagesGroups[groupId].Duplicates.Count > 1:
                {
                    if (groupsDone.Contains(groupId))
                        imagesGroups[groupId].SimilarImages.ExceptWith(groupsDone);
                    else
                    {
                        // Here an image has never been processed. It is either alone with no duplicates
                        // (delete) or it has multiple copies (associate them and delete) or it has
                        // similar images no matter how many copies (associate them and delete)
                        LinkImagesToParentGroup(groupId, imagesGroups, finalImages,
                            cancellationToken);

                        foreach (var similarImage in imagesGroups[groupId].SimilarImages)
                        {
                            var added = groupsDone.Add(similarImage);
                            if (added)
                                continue;

                            try
                            {
                                imagesGroups[similarImage].SimilarImages.ExceptWith(groupsDone);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine(e.Message);
                                Console.WriteLine(imagesGroups.ContainsKey(similarImage));
                            }
                        }

                        imagesGroups[groupId].SimilarImages.Clear();

                        await _notificationContext.Clients.All.SendAsync("notify",
                            new Notification(NotificationType.TotalProgress,
                                (++progress).ToString()), cancellationToken: cancellationToken);
                    }

                    break;
                }
                default:
                    imagesGroups[groupId].SimilarImages.Clear();
                    break;
            }

            if (decimal.Remainder(progress, notificationCeiling) == 0)
                GC.Collect();
        }
    }

    private void LinkImagesToParentGroup(string parentGroupId, Dictionary<string, ImagesGroup> imagesGroups,
        ConcurrentQueue<File> finalImages, CancellationToken token)
    {
        Parallel.ForEach(imagesGroups[parentGroupId].SimilarImages,
            new ParallelOptions { CancellationToken = token },
            similarImagesGroup =>
            {
                if (similarImagesGroup == parentGroupId)
                {
                    while (imagesGroups[parentGroupId].Duplicates.IsEmpty)
                    {
                        if (imagesGroups[parentGroupId].Duplicates.TryDequeue(out var image))
                            finalImages.Enqueue(new File
                            {
                                Path = image,
                                Size = imagesGroups[parentGroupId].Size,
                                DateModified = imagesGroups[parentGroupId].DateModified,
                                Hash = imagesGroups[parentGroupId].Id
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
                            Hash = parentGroupId
                        });
                    }
                }
            });
    }
}