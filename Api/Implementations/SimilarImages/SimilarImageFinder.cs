using System.Collections.Concurrent;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Aelian.FFT;
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
using DotNext.Threading;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Win32.SafeHandles;
using NSwag.Collections;
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
        string[] hypotheticalDuplicates, PerceptualHashAlgorithm? perceptualHashAlgorithm = null,
        decimal? degreeOfSimilarity = null, CancellationToken cancellationToken = default)
    {
        perceptualHashAlgorithm = PerceptualHashAlgorithm.QDctHash;
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

            await indexingRepository.DisableIndexingAsync(collectionName: CollectionName,
                cancellationToken: cancellationToken);

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
            } while (!await indexingRepository.IsIndexingDoneAsync(collectionName: CollectionName,
                         cancellationToken: cancellationToken));
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
                        perceptualHashAlgorithm: perceptualHashAlgorithm.Value,
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
        return finalImages.GroupBy(keySelector: file => file.Hash, comparer: HashComparer)
            .Where(predicate: i => i.Skip(count: 1).Any()).ToList();
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
                        duplicateImagesGroups, cancellationToken);

                    if (createdImagesGroup == null)
                        return;

                    var imageInfos = await imageInfosRepository.GetImageInfos(CollectionName,
                        createdImagesGroup.FileHash, imageHashGenerator.PerceptualHashAlgorithm, hashingToken);

                    int current;
                    if (imageInfos.ImageHash.Length != 0)
                    {
                        createdImagesGroup.ImageHash = imageInfos.ImageHash;
                        createdImagesGroup.Id = imageInfos.Id;
                        createdImagesGroup.IsCorruptedOrUnsupported = false;
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
                                imageHashGenerator.PerceptualHashAlgorithm, cancellationToken);
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
        SafeFileHandle fileHandle, ConcurrentDictionary<byte[], ImagesGroup> duplicateImagesGroups,
        CancellationToken cancellationToken = default)
    {
        var isFirst = duplicateImagesGroups.TryAdd(key: id, value: new ImagesGroup());
        var imagesGroup = duplicateImagesGroups[key: id];

        if (!isFirst)
            return null;

        imagesGroup.Duplicates.Push(item: path);
        imagesGroup.FileHash = id;
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
        ConcurrentDictionary<byte[], ImagesGroup> duplicateImagesGroups,
        PerceptualHashAlgorithm perceptualHashAlgorithm, decimal degreeOfSimilarity,
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
                            imagesGroup.FileHash, perceptualHashAlgorithm, similarityToken) ??
                        new ObservableDictionary<byte[], Similarity>(comparer: HashComparer);

                    // Check for new similar images excluding the ones cached in a previous search and add to cached ones
                    imagesGroup.Similarities.AddRange(items: new Dictionary<byte[], Similarity>(
                        collection: await similarImagesRepository.GetSimilarImages(collectionName: CollectionName,
                            id: imagesGroup.FileHash,
                            imageHash: imagesGroup.ImageHash!.Value,
                            perceptualHashAlgorithm: perceptualHashAlgorithm,
                            degreeOfSimilarity: degreeOfSimilarity,
                            groupsAlreadyDone: imagesGroup.Similarities.Keys, cancellationToken: similarityToken),
                        comparer: HashComparer));

                    // If there were new similar images, associate them to the imagesGroup
                    await similarImagesRepository.LinkToSimilarImagesAsync(CollectionName, imagesGroup.FileHash,
                        perceptualHashAlgorithm, imagesGroup.Similarities.Values, similarityToken);

                    imagesGroup.Similarities =
                        new ObservableDictionary<byte[], Similarity>(
                            dictionary: imagesGroup.Similarities
                                .Where(pair => pair.Value.Score <= degreeOfSimilarity)
                                .ToDictionary(comparer: HashComparer), comparer: HashComparer);

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
                foreach (var i in groupsDone)
                {
                    imagesGroup.Similarities.Remove(key: i);
                }

                if (imagesGroup.Similarities.Count == 0)
                {
                    continue;
                }

                imagesGroup.Similarities.TryAdd(key: imagesGroup.FileHash,
                    value: new Similarity
                        { OriginalId = imagesGroup.FileHash, DuplicateId = imagesGroup.FileHash, Score = 0 });

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

    private void SetCollectionChangedActionToDeleteGroupIfSimilarImagesEmpty(
        ConcurrentDictionary<byte[], ImagesGroup> duplicateImagesGroups, ImagesGroup imagesGroup)
    {
        imagesGroup.Similarities.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName != nameof(ObservableDictionary<byte[], Similarity>.Count) ||
                imagesGroup.Similarities.Count != 0)
                return;

            if (!duplicateImagesGroups.Remove(key: imagesGroup.FileHash, value: out _))
                Console.WriteLine(value: $"Removal failed for {imagesGroup.FileHash}");

            if (duplicateImagesGroups.Count % 1000 == 0)
                GC.Collect();
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
                            Hash = parentGroup.FileHash
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