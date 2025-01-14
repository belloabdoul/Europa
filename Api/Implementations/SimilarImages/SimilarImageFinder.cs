using System.Collections.Concurrent;
using System.Runtime;
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
using DotNext.Runtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Win32.SafeHandles;
using Swordfish.NET.Collections;
using File = Core.Entities.Files.File;
using UnboundedChannelOptions = System.Threading.Channels.UnboundedChannelOptions;

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
    IThumbnailGeneratorResolver thumbnailGeneratorResolver,
    [FromKeyedServices(PerceptualHashAlgorithm.QDctHash)]
    IImageHash imageHashGenerator,
    IHubContext<NotificationHub> notificationContext)
    : ISimilarFilesFinder
{
    private readonly IFileTypeIdentifier[] _imagesIdentifiers = fileTypeIdentifiers.ToArray();

    private static readonly HashComparer HashComparer = new();
    private const string CollectionName = "Europa-Images";

    public async Task<IEnumerable<IGrouping<byte[], File>>> FindSimilarFilesAsync(
        string[] hypotheticalDuplicates,
        decimal? degreeOfSimilarity = null, CancellationToken cancellationToken = default)
    {
        // Part 1 : Generate and cache perceptual hash of non-corrupted files
        var progressChannel = Channel.CreateUnboundedPrioritized(options: new UnboundedPrioritizedChannelOptions<int>
            { SingleReader = true, SingleWriter = false });

        var duplicateImagesGroups =
            new ConcurrentDictionary<byte[], ImagesGroup>(concurrencyLevel: Environment.ProcessorCount,
                capacity: hypotheticalDuplicates.Length,
                comparer: HashComparer);

        // Await the end of all tasks or the cancellation by the user
        try
        {
            await collectionRepository.CreateCollectionAsync(collectionName: CollectionName,
                cancellationToken: cancellationToken);

            await indexingRepository.DisableIndexingAsync(cancellationToken: cancellationToken);

            await Task.WhenAll(
                SendProgress(progressReader: progressChannel.Reader,
                    notificationType: NotificationType.HashGenerationProgress,
                    cancellationToken: cancellationToken),
                GeneratePerceptualHashes(hypotheticalDuplicates: hypotheticalDuplicates,
                    duplicateImagesGroups: duplicateImagesGroups, progressWriter: progressChannel.Writer,
                    cancellationToken: cancellationToken)
            );

            await indexingRepository.EnableIndexingAsync(collectionName: CollectionName,
                cancellationToken: cancellationToken);

            do
            {
                await Task.Delay(delay: TimeSpan.FromSeconds(5), cancellationToken: cancellationToken);
            } while (!await indexingRepository.IsIndexingDoneAsync(cancellationToken: cancellationToken));
        }
        catch (Exception e)
        {
            Console.WriteLine(value: e);
            duplicateImagesGroups.Clear();
            return [];
        }


        // Part 2 : Group similar images together
        progressChannel = Channel.CreateUnboundedPrioritized(options: new UnboundedPrioritizedChannelOptions<int>
            { SingleReader = true, SingleWriter = false });

        var groupingChannel = Channel.CreateUnbounded<byte[]>(options: new UnboundedChannelOptions
            { SingleReader = true, SingleWriter = false });

        var finalImages = new ConcurrentStack<File>();

        // Await the end of all tasks or the cancellation by the user
        try
        {
            await Task.WhenAll(
                tasks: new[]
                {
                    ProcessGroupsForFinalList(groupingChannelReader: groupingChannel.Reader,
                        duplicateImagesGroups: duplicateImagesGroups, finalImages: finalImages,
                        cancellationToken: cancellationToken),
                    SendProgress(progressReader: progressChannel.Reader,
                        notificationType: NotificationType.SimilaritySearchProgress,
                        cancellationToken: cancellationToken),
                    LinkSimilarImagesGroupsToOneAnother(duplicateImagesGroups: duplicateImagesGroups,
                        degreeOfSimilarity: degreeOfSimilarity!.Value, groupingChannelWriter: groupingChannel.Writer,
                        progressWriter: progressChannel.Writer, cancellationToken: cancellationToken)
                }
            );
        }
        catch (Exception)
        {
            duplicateImagesGroups.Clear();
            return [];
        }
        
        // Return images grouped by hashes
        var result = finalImages.GroupBy(keySelector: file => file.Hash, comparer: HashComparer)
            .Where(predicate: i => i.Skip(count: 1).Any()).ToList();
        
        return result;
    }

    private async Task GeneratePerceptualHashes(string[] hypotheticalDuplicates,
        ConcurrentDictionary<byte[], ImagesGroup> duplicateImagesGroups,
        ChannelWriter<int> progressWriter, CancellationToken cancellationToken = default)
    {
        var progress = 0;

        await Parallel.ForAsync(UIntPtr.Zero, hypotheticalDuplicates.GetLength(), cancellationToken,
            body: async (index, hashingToken) =>
            {
                var filePath = hypotheticalDuplicates[index];
                try
                {
                    var fileType = GetFileType(filePath, cancellationToken: hashingToken);

                    if (fileType is FileType.Animation)
                        return;

                    if (fileType is not (FileType.MagicScalerImage or FileType.LibRawImage
                        or FileType.LibVipsImage))
                    {
                        // await SendError(
                        //     message: $"File {filePath} is either of type unknown, corrupted or unsupported",
                        //     notificationContext: _notificationContext, cancellationToken: cancellationToken);
                        return;
                    }

                    using var fileHandle =
                        FileReader.GetFileHandle(path: filePath, sequential: true, isAsync: true);

                    // using var memoryMappedFile = FileReader.GetMemoryMappedFile(fileHandle: fileHandle);

                    var hash = await hashGenerator.GenerateHash(fileHandle, RandomAccess.GetLength(fileHandle),
                        cancellationToken: hashingToken);

                    if (hash.Length == 0)
                        return;

                    var createdImagesGroup = CreateGroup(hash, filePath, fileType, fileHandle,
                        duplicateImagesGroups);

                    if (createdImagesGroup == null)
                        return;

                    createdImagesGroup.ImageHash = await imageInfosRepository.GetImageHash(CollectionName,
                        createdImagesGroup.Id, hashingToken);

                    int current;
                    if (createdImagesGroup.ImageHash.Value.Length != 0)
                    {
                        current = Interlocked.Increment(ref progress);
                        await progressWriter.WriteAsync(current, cancellationToken: hashingToken);
                        return;
                    }

                    var thumbnailGenerator =
                        thumbnailGeneratorResolver.GetThumbnailGenerator(createdImagesGroup.FileType);

                    using var pixels = MemoryOwner<byte>.Allocate(imageHashGenerator.ColorSpace switch
                    {
                        ColorSpace.Grayscale => imageHashGenerator.ImageSize * sizeof(float),
                        _ => imageHashGenerator.ImageSize * 3 * sizeof(float)
                    });

                    if (thumbnailGenerator.GenerateThumbnail(filePath, imageHashGenerator.Width,
                            imageHashGenerator.Height, MemoryMarshal.Cast<byte, float>(span: pixels.Span),
                            imageHashGenerator.ColorSpace))
                    {
                        createdImagesGroup.ImageHash =
                            imageHashGenerator.GenerateHash(
                                pixels: MemoryMarshal.Cast<byte, float>(span: pixels.Span));
                        if (createdImagesGroup.ImageHash.HasValue && createdImagesGroup.ImageHash.Value.Length != 0)
                        {
                            await imageInfosRepository.InsertImageInfos(CollectionName, createdImagesGroup,
                                cancellationToken);
                            current = Interlocked.Increment(ref progress);
                            await progressWriter.WriteAsync(current, cancellationToken: hashingToken);
                        }
                        else
                            Console.WriteLine(value: $"Failed to generate thumbnail for {filePath}");
                    }
                }
                catch (IOException)
                {
                    await SendError(message: $"File {filePath} is being used by another application",
                        notificationContext: notificationContext, cancellationToken: hashingToken);
                }
                catch (Exception e)
                {
                    Console.WriteLine(value: e);
                    throw;
                }
            });


        progressWriter.Complete();
    }

    private FileType GetFileType(string hypotheticalDuplicate, CancellationToken cancellationToken = default)
    {
        // Go through every image identifiers to get the one to use.
        // If the file is not supported send a message, else send the
        // image for the next step which is hash generation and grouping

        FileType fileType;
        var index = 0;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            fileType = _imagesIdentifiers[index].GetFileType(path: hypotheticalDuplicate);
            index++;
        } while (fileType is FileType.CorruptUnknownOrUnsupported &&
                 index < _imagesIdentifiers.Length);

        return fileType;
    }

    private static ImagesGroup? CreateGroup(byte[] id, string path, FileType fileType,
        SafeFileHandle fileHandle, ConcurrentDictionary<byte[], ImagesGroup> duplicateImagesGroups)
    {
        var isFirst = duplicateImagesGroups.TryAdd(key: id, value: new ImagesGroup());
        var imagesGroup = duplicateImagesGroups[key: id];

        if (!isFirst)
            return null;

        imagesGroup.Duplicates.Push(item: path);
        imagesGroup.Id = id;
        imagesGroup.Size = RandomAccess.GetLength(fileHandle);
        imagesGroup.FileType = fileType;
        imagesGroup.DateModified = System.IO.File.GetLastWriteTime(fileHandle);
        return imagesGroup;
    }

    private static Task SendError(string message, IHubContext<NotificationHub> notificationContext,
        CancellationToken cancellationToken)
    {
        return notificationContext.Clients.All.SendAsync(method: "notify",
            arg1: new Notification(type: NotificationType.Exception, result: message),
            cancellationToken: cancellationToken);
    }

    private async Task SendProgress(ChannelReader<int> progressReader, NotificationType notificationType,
        CancellationToken cancellationToken)
    {
        var progress = 0;
        await foreach (var hashProcessed in progressReader.ReadAllAsync(cancellationToken: cancellationToken))
        {
            progress = hashProcessed;

            if (progress % 100 != 0)
                continue;

            if (progress % 1000 == 0)
            {
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(2, GCCollectionMode.Aggressive, true, true);
            }

            await notificationContext.Clients.All.SendAsync(method: "notify",
                arg1: new Notification(type: notificationType, result: progress.ToString()),
                cancellationToken: cancellationToken);
        }

        await notificationContext.Clients.All.SendAsync(method: "notify",
            arg1: new Notification(type: notificationType, result: progress.ToString()),
            cancellationToken: cancellationToken);
    }

    private async Task LinkSimilarImagesGroupsToOneAnother(
        ConcurrentDictionary<byte[], ImagesGroup> duplicateImagesGroups, decimal degreeOfSimilarity,
        ChannelWriter<byte[]> groupingChannelWriter, ChannelWriter<int> progressWriter,
        CancellationToken cancellationToken)
    {
        var progress = 0;

        var keys = duplicateImagesGroups.Keys.ToArray();

        await Parallel.ForEachAsync(source: keys, cancellationToken,
            body: async (key, similarityToken) =>
            {
                var imagesGroup = duplicateImagesGroups[key: key];
                try
                {
                    // Get cached similar images
                    imagesGroup.Similarities =
                        await similarImagesRepository.GetExistingSimilaritiesForImage(CollectionName,
                            imagesGroup.Id, similarityToken);

                    // Check for new similar images excluding the ones cached in a previous search and add to cached ones
                    imagesGroup.Similarities.AddRange(await similarImagesRepository.GetSimilarImages(
                        collectionName: CollectionName,
                        id: imagesGroup.Id,
                        imageHash: imagesGroup.ImageHash!.Value,
                        degreeOfSimilarity: degreeOfSimilarity,
                        groupsAlreadyDone: imagesGroup.Similarities.Keys, cancellationToken: similarityToken));

                    // If there were new similar images, associate them to the imagesGroup
                    await similarImagesRepository.LinkToSimilarImagesAsync(CollectionName, imagesGroup.Id,
                        imagesGroup.Similarities.Values, similarityToken);

                    var results = imagesGroup.Similarities
                        .Where(pair => pair.Value.Score <= degreeOfSimilarity);
                    
                    imagesGroup.Similarities =
                        new ConcurrentObservableDictionary<byte[], Similarity>(false, HashComparer);
                    imagesGroup.Similarities.AddRange(results);

                    // Send progress
                    var current = Interlocked.Increment(ref progress);
                    await progressWriter.WriteAsync(current, cancellationToken: similarityToken);

                    // Queue to the next step
                    await groupingChannelWriter.WriteAsync(item: key, cancellationToken: similarityToken);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            });

        progressWriter.Complete();
        groupingChannelWriter.Complete();
    }

    private async Task ProcessGroupsForFinalList(ChannelReader<byte[]> groupingChannelReader,
        ConcurrentDictionary<byte[], ImagesGroup> duplicateImagesGroups, ConcurrentStack<File> finalImages,
        CancellationToken cancellationToken)
    {
        var progress = 0;

        var groupsDone = new HashSet<byte[]>(comparer: HashComparer);

        await foreach (var groupId in groupingChannelReader.ReadAllAsync(
                           cancellationToken: cancellationToken))
        {
            try
            {
                var imagesGroup = duplicateImagesGroups[key: groupId];

                // Set the images imagesGroup for removal if its list of similar images is empty
                SetCollectionChangedActionToDeleteGroupIfSimilarImagesEmpty(
                    duplicateImagesGroups: duplicateImagesGroups, imagesGroup: imagesGroup);

                // Removes the groups already done in the current imagesGroup's similar images groups. If the similar images are
                // empty we stop here, else we add back the current imagesGroup's id in case it was among those deleted

                imagesGroup.Similarities.RemoveRange(groupsDone);

                if (imagesGroup.Similarities.Count == 0)
                {
                    continue;
                }

                imagesGroup.Similarities.TryAdd(key: imagesGroup.Id,
                    value: new Similarity
                        { OriginalId = imagesGroup.Id, DuplicateId = imagesGroup.Id, Score = 0 });

                // Here an image was either never processed or has remaining similar images groups. If the remaining groups
                // are only itself and there is only one duplicate we stop here
                if (imagesGroup.Similarities.Count == 1 && imagesGroup.Duplicates.Count == 1)
                {
                    imagesGroup.Similarities.Clear();
                    continue;
                }

                // Here there are either multiple images imagesGroup remaining or it is a single image with multiple duplicates
                // We associate them to one another.
                LinkImagesToParentGroup(parentGroupId: groupId, duplicateImagesGroups: duplicateImagesGroups,
                    finalImages: finalImages, cancellationToken: cancellationToken);

                // After associating the current image with its remaining similar images, we add them to the list of already
                // processed images
                foreach (var id in imagesGroup.Similarities.Keys)
                {
                    groupsDone.Add(item: id);
                }

                imagesGroup.Similarities.Clear();

                progress++;

                if (progress % 100 != 0)
                    continue;

                await notificationContext.Clients.All.SendAsync(method: "notify",
                    arg1: new Notification(type: NotificationType.TotalProgress, result: progress.ToString()),
                    cancellationToken: cancellationToken);
            }
            catch (Exception e)
            {
                Console.WriteLine(value: e);
            }
        }

        await notificationContext.Clients.All.SendAsync(method: "notify",
            arg1: new Notification(type: NotificationType.TotalProgress, result: progress.ToString()),
            cancellationToken: cancellationToken);
    }

    private static void SetCollectionChangedActionToDeleteGroupIfSimilarImagesEmpty(
        ConcurrentDictionary<byte[], ImagesGroup> duplicateImagesGroups, ImagesGroup imagesGroup)
    {
        imagesGroup.Similarities.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName != nameof(ConcurrentObservableDictionary<byte[], Similarity>.Count) ||
                imagesGroup.Similarities.Count != 0)
                return;

            duplicateImagesGroups.TryRemove(key: imagesGroup.Id, value: out _);
        };
    }

    private static void LinkImagesToParentGroup(byte[] parentGroupId,
        ConcurrentDictionary<byte[], ImagesGroup> duplicateImagesGroups, ConcurrentStack<File> finalImages,
        CancellationToken cancellationToken)
    {
        // Associated the current imagesGroup of images with its similar imagesGroup of images.
        // In the case of the current imagesGroup, it will not be used after so we dequeue
        // each file's path. For the similar images they will not be dequeued here.
        var parentGroup = duplicateImagesGroups[key: parentGroupId];
        Parallel.ForEach(source: parentGroup.Similarities.Keys,
            parallelOptions: new ParallelOptions { CancellationToken = cancellationToken },
            body: imageGroupId =>
            {
                if (imageGroupId.AsSpan().SequenceEqual(other: parentGroupId))
                {
                    while (parentGroup.Duplicates.TryPop(result: out var image))
                    {
                        finalImages.Push(item: new File
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
                    if (!duplicateImagesGroups.TryGetValue(key: imageGroupId, value: out var imagesGroup))
                        return;

                    foreach (var image in imagesGroup.Duplicates)
                    {
                        finalImages.Push(item: new File
                        {
                            Path = image,
                            Size = imagesGroup.Size,
                            DateModified = imagesGroup.DateModified,
                            Hash = parentGroupId
                        });
                    }
                }
            });
    }
}