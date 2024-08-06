using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Threading.Channels;
using API.Implementations.Common;
using Core.Entities;
using Core.Entities.Redis;
using Core.Interfaces;
using Core.Interfaces.Common;
using Database.Interfaces;
using Microsoft.AspNetCore.SignalR;
using Redis.OM;
using File = Core.Entities.File;

namespace API.Implementations.SimilarImages;

public class SimilarImageFinder : ISimilarFilesFinder
{
    private readonly double _degreeOfSimilarity;
    private readonly IFileTypeIdentifier _fileTypeIdentifier;
    private readonly IHashGenerator _hashGenerator;

    private readonly IDbHelpers _dbHelpers;
    private readonly IImageHash _imageHashGenerator;
    private readonly IHubContext<NotificationHub> _notificationContext;

    public SimilarImageFinder(double degreeOfSimilarity, IHubContext<NotificationHub> notificationContext,
        IFileTypeIdentifier fileTypeIdentifier, IHashGenerator hashGenerator, IImageHash imageHashGenerator,
        IDbHelpers dbHelpers)
    {
        _degreeOfSimilarity = degreeOfSimilarity;
        _notificationContext = notificationContext;
        _fileTypeIdentifier = fileTypeIdentifier;
        _hashGenerator = hashGenerator;
        _imageHashGenerator = imageHashGenerator;
        _dbHelpers = dbHelpers;
    }

    public async Task<IEnumerable<IGrouping<string, File>>> FindSimilarFilesAsync(
        string[] hypotheticalDuplicates, CancellationToken cancellationToken)
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

        using var similarityTask = LinkSimilarImagesGroupsToOneAnother(imagesGroups, _degreeOfSimilarity,
            groupingChannel.Writer, cancellationToken: cancellationToken);

        await Task.WhenAll(similarityTask, groupingTask);

        var groups = finalImages.GroupBy(file => file.Hash)
            .Where(i => i.Count() != 1).ToList();
        return groups;
    }

    private async Task<ConcurrentDictionary<string, ImagesGroup>> GenerateImageHashForNonCorruptedFiles(
        string[] hypotheticalDuplicates, CancellationToken cancellationToken)
    {
        var progress = 0;

        var copiesGroups =
            new ConcurrentDictionary<string, ImagesGroup>(Environment.ProcessorCount, hypotheticalDuplicates.Length);

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
                            await SendMessage(
                                $"File {hypotheticalDuplicate} is either of type unknown, corrupted or unsupported",
                                NotificationType.Exception, cancellationToken: hashingToken);
                            break;
                        case FileType.Image:
                        {
                            using var fileHandle =
                                FileReader.GetFileHandle(hypotheticalDuplicate, isAsync: true);

                            var length = RandomAccess.GetLength(fileHandle);

                            var hash = await _hashGenerator.GenerateHashAsync(fileHandle, length,
                                cancellationToken: hashingToken);

                            if (string.IsNullOrEmpty(hash))
                            {
                                await SendMessage(
                                    $"File {hypotheticalDuplicate} is either of type unknown, corrupted or unsupported",
                                    NotificationType.Exception, cancellationToken: hashingToken);
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
                                        for (var i = 0; i < group.Duplicates.Count; i++)
                                        {
                                            group.Duplicates.TryDequeue(out var duplicate);
                                            await SendMessage(
                                                $"File {duplicate} is either of type unknown, corrupted or unsupported",
                                                NotificationType.Exception, cancellationToken: hashingToken);
                                        }
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

                                    await SendMessage(progress.ToString(), NotificationType.HashGenerationProgress,
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
                catch (IOException)
                {
                    await SendMessage($"File {hypotheticalDuplicate} is being used by another application",
                        NotificationType.Exception, cancellationToken: hashingToken);
                }
            });

        return copiesGroups;
    }

    private async ValueTask SendMessage(string message, NotificationType notificationType,
        CancellationToken cancellationToken)
    {
        await _notificationContext.Clients.All.SendAsync("notify",
            new Notification(notificationType, message), cancellationToken: cancellationToken);
    }

    private async Task LinkSimilarImagesGroupsToOneAnother(ConcurrentDictionary<string, ImagesGroup> imagesGroups,
        double degreeOfSimilarity, ChannelWriter<string> groupingChannelWriter,
        CancellationToken cancellationToken)
    {
        var progress = 0;

        var keys = imagesGroups.Keys.ToArray();

        await Parallel.ForEachAsync(keys, new ParallelOptions { CancellationToken = cancellationToken },
            async (key, similarityToken) =>
            {
                var group = imagesGroups[key];

                // Get cached similar images
                group.SimilarImages = await _dbHelpers.GetSimilarImagesAlreadyDoneInRange(group.Id);

                // In case in the next step all similar images have been removed, remove group from dictionary to free
                // memory
                group.SimilarImages.CollectionChanged += (sender, args) =>
                {
                    if (args.Action == NotifyCollectionChangedAction.Replace && group.SimilarImages.Count == 0)
                        imagesGroups.Remove(group.Id, out _);
                };

                // Check for new similar images excluding the ones cached in a previous search and add to cached ones
                group.Similarities = await _dbHelpers.GetSimilarImages(group.Id, group.ImageHash, degreeOfSimilarity,
                    group.SimilarImages);

                var isEmpty = group.SimilarImages.Count == 0;

                foreach (var similarity in group.Similarities)
                    group.SimilarImages.Add(similarity.DuplicateId);

                // If there were new similar images, associate them to the group
                if (group.Similarities.Count > 0)
                    await _dbHelpers.LinkToSimilarImagesAsync(group.Id, group.Similarities, isEmpty);

                // Send progress
                var current = Interlocked.Increment(ref progress);

                await SendMessage(progress.ToString(), NotificationType.SimilaritySearchProgress,
                    cancellationToken: similarityToken);

                // Queue to the next step
                await groupingChannelWriter.WriteAsync(key, cancellationToken: similarityToken);

                if (current % 1000 == 0)
                    GC.Collect();
            });

        groupingChannelWriter.Complete();
    }

    private async Task ProcessGroupsForFinalList(ChannelReader<string> groupingChannelReader,
        ConcurrentDictionary<string, ImagesGroup> imagesGroups, ConcurrentQueue<File> finalImages,
        CancellationToken cancellationToken)
    {
        var progress = 0;

        var notificationCeiling = decimal.Divide(imagesGroups.Count, 400);

        var groupsDone = new HashSet<string>();

        await foreach (var groupId in groupingChannelReader.ReadAllAsync(
                           cancellationToken: cancellationToken))
        {
            var group = imagesGroups[groupId];

            // Remove similar images which have been processed before this one
            group.SimilarImages.ExceptWith(groupsDone);

            // If among these removed images there is the current image's own id, it will be skipped and will only be
            // removed if there are no other similar images associated
            if (group.SimilarImages.Count == 0)
                continue;

            group.SimilarImages.Add(group.Id);

            // Here an image has never been processed. It is either alone with no duplicates
            // (delete) or it has multiple copies (associate them then delete)
            if (group.SimilarImages.Count == 1 && group.Duplicates.Count == 1)
            {
                group.SimilarImages.Clear();
                continue;
            }

            // Here is where the association is done
            LinkImagesToParentGroup(groupId, imagesGroups, finalImages,
                cancellationToken);

            // After associating the current images with its remaining similar images, we add them to the list of already
            // processed images. We also make use of this opportunity to check if among them there are skipped images
            // and if so, we remove its similar files.
            foreach (var similarImage in group.SimilarImages)
                groupsDone.Add(similarImage);

            group.SimilarImages.Clear();

            await SendMessage((++progress).ToString(), NotificationType.TotalProgress,
                cancellationToken: cancellationToken);

            Console.WriteLine();

            if (decimal.Remainder(progress, notificationCeiling) == 0)
                GC.Collect();
        }
    }

    private void LinkImagesToParentGroup(string parentGroupId, ConcurrentDictionary<string, ImagesGroup> imagesGroups,
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