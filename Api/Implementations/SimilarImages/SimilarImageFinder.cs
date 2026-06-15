using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Api.Client.Repositories;
using Api.Implementations.Commons;
using CommunityToolkit.HighPerformance.Buffers;
using Core.Entities.Commons;
using Core.Entities.Files;
using Core.Entities.Images;
using Core.Entities.Notifications;
using Core.Entities.SearchParameters;
using Core.Interfaces.Commons;
using Core.Interfaces.SimilarImages;
using DotNext.Collections.Generic;
using Microsoft.AspNetCore.SignalR;
using ToolBX.Collections.ObservableDictionary;
using File = Core.Entities.Files.File;

namespace Api.Implementations.SimilarImages;

public class SimilarImageFinder(
    [FromKeyedServices(FileSearchType.Images)]
    ICollectionRepository collectionRepository,
    [FromKeyedServices(FileSearchType.Images)]
    IIndexingRepository indexingRepository,
    IImageInfosRepository imageInfosRepository,
    ISimilarImagesRepository similarImagesRepository,
    [FromKeyedServices(FileSearchType.Images)]
    IEnumerable<IFileTypeIdentifier> fileTypeIdentifiers,
    IHashGenerator hashGenerator,
    [FromKeyedServices(PerceptualHashAlgorithm.QDftHash)]
    IImageHash imageHashGenerator,
    IHubContext<NotificationHub> notificationContext)
    : ISimilarFilesFinder
{
    private static readonly ByteArrayComparer ByteArrayComparer = new();

    public async Task<IEnumerable<IGrouping<byte[], File>>> FindSimilarFilesAsync(
        List<string> hypotheticalDuplicates, decimal? degreeOfSimilarity = null,
        CancellationToken cancellationToken = default)
    {
        // Part 1 : Generate and cache perceptual hash of non-corrupted files
        var progressChannel = Channel.CreateUnboundedPrioritized(new UnboundedPrioritizedChannelOptions<int>
            { SingleReader = true, SingleWriter = false });

        // Await the end of all tasks or the cancellation by the user
        ConcurrentDictionary<long, ImagesGroup> duplicateImagesGroups;
        try
        {
            await collectionRepository.CreateTablesAsync(cancellationToken);

            await indexingRepository.DisableIndexingAsync(cancellationToken);

            var progressTask = SendProgress(progressChannel.Reader, NotificationType.HashGenerationProgress,
                cancellationToken);

            var hashGenerationTask =
                GeneratePerceptualHashes(hypotheticalDuplicates, progressChannel.Writer, cancellationToken);
            await Task.WhenAll(progressTask, hashGenerationTask);

            await indexingRepository.EnableIndexingAsync(cancellationToken);

            do
            {
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            } while (!await indexingRepository.IsIndexingDoneAsync(cancellationToken));

            duplicateImagesGroups = hashGenerationTask.Result;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return [];
        }
        
        Console.WriteLine(duplicateImagesGroups.Count);

        // Part 2 : Group similar images together
        progressChannel = Channel.CreateUnboundedPrioritized(new UnboundedPrioritizedChannelOptions<int>
            { SingleReader = true, SingleWriter = false });
        
        var groupingChannel = Channel.CreateUnbounded<long>(new UnboundedChannelOptions
            { SingleReader = true, SingleWriter = false });
        
        var finalImages = new ConcurrentStack<File>();
        
        // Await the end of all tasks or the cancellation by the user
        try
        {
            await Task.WhenAll(
                new[]
                {
                    ProcessGroupsForFinalList(groupingChannel.Reader, duplicateImagesGroups, finalImages,
                        cancellationToken),
                    SendProgress(progressChannel.Reader, NotificationType.SimilaritySearchProgress, cancellationToken),
                    FindAndLinkSimilarImagesGroups(duplicateImagesGroups, degreeOfSimilarity!.Value,
                        similarImagesRepository, groupingChannel.Writer, progressChannel.Writer, cancellationToken)
                }
            );
        }
        catch (Exception)
        {
            return [];
        }
        
        // Return images grouped by hashes
        var result = finalImages.GroupBy(file => file.Hash, ByteArrayComparer)
            .Where(i => i.Skip(1).Any()).ToList();
        
        return result;
    }

    private async Task<ConcurrentDictionary<long, ImagesGroup>>
        GeneratePerceptualHashes(List<string> hypotheticalDuplicates,
            ChannelWriter<int> progressWriter, CancellationToken cancellationToken = default)
    {
        using var progress = MemoryOwner<int>.Allocate(1, ArrayPool<int>.Shared);
        progress.Span.Clear();
        var duplicateImagesGroups =
            new ConcurrentDictionary<byte[], ImagesGroup>(
                Environment.ProcessorCount, hypotheticalDuplicates.Count, ByteArrayComparer);


        var length = hypotheticalDuplicates.Count;
        await Parallel.ForAsync(0, length, cancellationToken,
            (index, hashingToken) => GeneratePerceptualHashAsync(hypotheticalDuplicates[index], fileTypeIdentifiers,
                hashGenerator, duplicateImagesGroups, imageInfosRepository, imageHashGenerator,
                progressWriter, progress.Memory, notificationContext, hashingToken));


        progressWriter.Complete();
        return new ConcurrentDictionary<long, ImagesGroup>(
            duplicateImagesGroups.Where(group => group.Key.Length != 0 && group.Value.Id != 0)
                .ToDictionary(val => val.Value.Id, val => val.Value));
    }

    private static async ValueTask GeneratePerceptualHashAsync(string hypotheticalDuplicate,
        IEnumerable<IFileTypeIdentifier> fileTypeIdentifiers, IHashGenerator hashGenerator,
        ConcurrentDictionary<byte[], ImagesGroup> duplicateImagesGroups,
        IImageInfosRepository imageInfosRepository, IImageHash imageHashGenerator, ChannelWriter<int> progressWriter,
        ReadOnlyMemory<int> progress, IHubContext<NotificationHub> notificationContext,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var fileType = await GetFileType(hypotheticalDuplicate, fileTypeIdentifiers, cancellationToken);

            if (fileType is FileType.Animation)
                return;

            if (fileType is not (FileType.MagicScalerImage or FileType.LibRawImage
                or FileType.LibVipsImage or FileType.PFimImage))
            {
                await SendError($"File {hypotheticalDuplicate} is either of type unknown, corrupted or unsupported",
                    notificationContext, cancellationToken);
                return;
            }

            var size = 0L;
            var hash = await hashGenerator.GenerateHashAsync(hypotheticalDuplicate, length =>
            {
                size = length;
                return length;
            }, cancellationToken);

            if (hash.Length == 0)
                return;

            var createdImagesGroup = CreateGroup(hash, hypotheticalDuplicate, fileType, size, duplicateImagesGroups,
                cancellationToken);

            if (createdImagesGroup == null)
                return;

            var imageInfos = await imageInfosRepository.GetImageInfos(createdImagesGroup.FileHash, cancellationToken);

            int current;
            if (imageInfos.Id != 0)
            {
                createdImagesGroup.Id = imageInfos.Id;
                createdImagesGroup.Hash = imageInfos.ImageHash;
                current = Interlocked.Increment(ref MemoryMarshal.GetReference(progress.Span));
                await progressWriter.WriteAsync(current, cancellationToken);
                return;
            }

            createdImagesGroup.Hash =
                imageHashGenerator.GenerateHash(hypotheticalDuplicate, createdImagesGroup.FileType);

            if (createdImagesGroup.Hash.Length != 0)
            {
                createdImagesGroup.Id = await imageInfosRepository.InsertImageInfos(createdImagesGroup, cancellationToken);
                current = Interlocked.Increment(ref MemoryMarshal.GetReference(progress.Span));
                await progressWriter.WriteAsync(current, cancellationToken);
            }
            else
                Console.WriteLine($"Failed to generate thumbnail for {hypotheticalDuplicate}");
        }
        catch (IOException)
        {
            await SendError($"File {hypotheticalDuplicate} is being used by another application", notificationContext,
                cancellationToken);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }

    private static async ValueTask<FileType> GetFileType(string hypotheticalDuplicate,
        IEnumerable<IFileTypeIdentifier> imagesIdentifiers,
        CancellationToken cancellationToken = default)
    {
        // Go through every image identifiers to get the one to use.
        // If the file is not supported send a message, else send the
        // image for the next step which is hash generation and grouping

        var fileType = FileType.CorruptUnknownOrUnsupported;
        await using var imageIdentifier = imagesIdentifiers.GetAsyncEnumerator(cancellationToken);
        while (fileType is FileType.CorruptUnknownOrUnsupported && await imageIdentifier.MoveNextAsync())
        {
            fileType = imageIdentifier.Current.GetFileType(hypotheticalDuplicate);
        }

        return fileType;
    }

    private static ImagesGroup? CreateGroup(byte[] id, string path, FileType fileType, long length,
        ConcurrentDictionary<byte[], ImagesGroup> duplicateImagesGroups, CancellationToken cancellationToken = default)
    {
        var isFirst = duplicateImagesGroups.TryAdd(id, new ImagesGroup());

        if (!isFirst)
            return null;

        var imagesGroup = duplicateImagesGroups[id];
        imagesGroup.Duplicates.Push(path);
        imagesGroup.FileHash = id;
        imagesGroup.Size = length;
        imagesGroup.FileType = fileType;
        imagesGroup.DateModified = System.IO.File.GetLastWriteTime(path);
        return imagesGroup;
    }

    private static Task SendError(string message, IHubContext<NotificationHub> notificationContext,
        CancellationToken cancellationToken)
    {
        return notificationContext.Clients.All.SendAsync("notify",
            new Notification(NotificationType.Exception, message), cancellationToken);
    }

    private async Task SendProgress(ChannelReader<int> progressReader, NotificationType notificationType,
        CancellationToken cancellationToken)
    {
        var progress = 0;
        await foreach (var hashProcessed in progressReader.ReadAllAsync(cancellationToken))
        {
            progress = hashProcessed;

            if (progress % 100 != 0)
                continue;

            await notificationContext.Clients.All.SendAsync("notify",
                new Notification(notificationType, progress.ToString()), cancellationToken);
        }

        await notificationContext.Clients.All.SendAsync("notify",
            new Notification(notificationType, progress.ToString()), cancellationToken);
    }

    private static async Task FindAndLinkSimilarImagesGroups(
        ConcurrentDictionary<long, ImagesGroup> duplicateImagesGroups,
        decimal degreeOfSimilarity, ISimilarImagesRepository similarImagesRepository,
        ChannelWriter<long> groupingChannelWriter, ChannelWriter<int> progressWriter,
        CancellationToken cancellationToken)
    {
        var progress = 0;

        await Parallel.ForEachAsync(duplicateImagesGroups, cancellationToken,
            async (imagesGroup, similarityToken) =>
            {
                var group = imagesGroup.Value;
                var id = imagesGroup.Key;
                try
                {
                    // Get existing similar images meeting the threshold set
                    group.Matches =
                        await similarImagesRepository.GetExistingSimilaritiesForImage(id,
                            similarityToken);

                    // If no similar images is found add the current image with its score
                    if (group.Matches.Count == 0)
                        group.Matches.TryAdd(id, new Similarity { OriginalId = id, DuplicateId = id, Score = 0 });

                    // Check for new similar images excluding the ones cached in a previous search and add to cached ones
                    await foreach (var similarity in similarImagesRepository.GetSimilarImages(
                                       id,
                                       group.Hash!,
                                       degreeOfSimilarity,
                                       group.Matches.Keys.ToList(), similarityToken))
                        group.Matches.TryAdd(similarity.Key, similarity.Value);

                    // If there were new similar images, associate them to the imagesGroup
                    await similarImagesRepository.LinkToSimilarImagesAsync(id, group.Matches.Values, similarityToken);

                    // Send progress
                    var current = Interlocked.Increment(ref progress);
                    await progressWriter.WriteAsync(current, similarityToken);

                    // Queue to the next step
                    await groupingChannelWriter.WriteAsync(id, similarityToken);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            });

        progressWriter.Complete();
        groupingChannelWriter.Complete();
    }

    private async Task ProcessGroupsForFinalList(ChannelReader<long> groupingChannelReader,
        ConcurrentDictionary<long, ImagesGroup> duplicateImagesGroups,
        ConcurrentStack<File> finalImages,
        CancellationToken cancellationToken)
    {
        var progress = 0;

        var groupsDone = new HashSet<long>();

        await foreach (var groupId in groupingChannelReader.ReadAllAsync(cancellationToken))
        {
            try
            {
                duplicateImagesGroups.TryGetValue(groupId, out var imagesGroup);

                if (imagesGroup!.Matches.Count == 1 && imagesGroup.Duplicates.Count == 1)
                {
                    imagesGroup.Matches.Clear();
                    continue;
                }

                // Set the images imagesGroup for removal if its list of similar images is empty
                SetCollectionChangedActionToDeleteGroupIfSimilarImagesEmpty(duplicateImagesGroups, imagesGroup);

                // Removes the groups already done in the current imagesGroup's similar images groups. If the similar images are
                // empty we stop here, else we add back the current imagesGroup's id in case it was among those deleted
                imagesGroup.Matches.TryRemove(group => groupsDone.Contains(group.Key));

                if (imagesGroup.Matches.Count == 0)
                {
                    continue;
                }

                imagesGroup.Matches.TryAdd(imagesGroup.Id,
                    new Similarity
                        { OriginalId = imagesGroup.Id, DuplicateId = imagesGroup.Id, Score = 0 });

                // Here an image was either never processed or has remaining similar images groups. If the remaining groups
                // are only itself and there is only one duplicate we stop here
                if (imagesGroup.Matches.Count == 1 && imagesGroup.Duplicates.Count == 1)
                {
                    imagesGroup.Matches.Clear();
                    continue;
                }

                // Here there are either multiple images imagesGroup remaining or it is a single image with multiple duplicates
                // We associate them to one another.
                await LinkImagesToParentGroup(groupId, duplicateImagesGroups,
                    finalImages, cancellationToken);

                // After associating the current image with its remaining similar images, we add them to the list of already
                // processed images
                foreach (var id in imagesGroup.Matches.Keys)
                {
                    groupsDone.Add(id);
                }

                imagesGroup.Matches.Clear();

                progress++;

                if (progress % 100 != 0)
                    continue;

                await notificationContext.Clients.All.SendAsync("notify",
                    new Notification(NotificationType.TotalProgress, progress.ToString()),
                    cancellationToken);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        await notificationContext.Clients.All.SendAsync("notify",
            new Notification(NotificationType.TotalProgress, progress.ToString()),
            cancellationToken);
    }

    private static void SetCollectionChangedActionToDeleteGroupIfSimilarImagesEmpty(
        ConcurrentDictionary<long, ImagesGroup> duplicateImagesGroups, ImagesGroup imagesGroup)
    {
        imagesGroup.Matches.CollectionChanged += (sender, _) =>
        {
            if (((ObservableDictionary<long, Similarity>)sender).Count != 0)
                return;

            duplicateImagesGroups.TryRemove(imagesGroup.Id);
        };
    }

    private static Task LinkImagesToParentGroup(long parentGroupId,
        ConcurrentDictionary<long, ImagesGroup> duplicateImagesGroups,
        ConcurrentStack<File> finalImages, CancellationToken cancellationToken)
    {
        // Associated the current imagesGroup of images with its similar imagesGroup of images.
        // In the case of the current imagesGroup, it will not be used after so we dequeue
        // each file's path. For the similar images they will not be dequeued here.
        var parentGroup = duplicateImagesGroups[parentGroupId];
        return Parallel.ForEachAsync(parentGroup.Matches, cancellationToken,
            (group, _) =>
            {
                var imageGroupId = group.Key;
                if (imageGroupId == parentGroup.Id)
                {
                    while (parentGroup.Duplicates.TryPop(out var image))
                    {
                        finalImages.Push(new File
                        {
                            Path = image,
                            Size = parentGroup.Size,
                            DateModified = parentGroup.DateModified,
                            Hash = parentGroup.FileHash
                        });
                    }
                }
                else
                {
                    if (!duplicateImagesGroups.TryGetValue(imageGroupId, out var imagesGroup))
                        return ValueTask.CompletedTask;

                    foreach (var image in imagesGroup.Duplicates)
                    {
                        finalImages.Push(new File
                        {
                            Path = image,
                            Size = imagesGroup.Size,
                            DateModified = imagesGroup.DateModified,
                            Hash = parentGroup.FileHash
                        });
                    }
                }

                return ValueTask.CompletedTask;
            });
    }
}