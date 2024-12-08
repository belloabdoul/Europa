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
using Microsoft.AspNetCore.SignalR;
using Microsoft.Win32.SafeHandles;
using NSwag.Collections;
using File = Core.Entities.Files.File;

namespace Api.Implementations.SimilarImages;

public class SimilarImageFinder : ISimilarFilesFinder
{
    private readonly ICollectionRepository _collectionRepository;
    private readonly IIndexingRepository _indexingRepository;
    private readonly IImageInfosRepository _imageInfosRepository;
    private readonly ISimilarImagesRepository _similarImagesRepository;

    private readonly IFileTypeIdentifier[] _imagesIdentifiers;

    private readonly IHashGenerator _hashGenerator;
    private readonly IImageHash _imageHashGenerator;
    private readonly IThumbnailGeneratorResolver _thumbnailGeneratorResolver;
    private readonly IHubContext<NotificationHub> _notificationContext;

    private static readonly HashComparer HashComparer = new();
    private const string CollectionName = nameof(ImagesGroup);

    public SimilarImageFinder([FromKeyedServices(FileSearchType.Images)] ICollectionRepository collectionRepository,
        IIndexingRepository indexingRepository,
        IImageInfosRepository imageInfosRepository,
        ISimilarImagesRepository similarImagesRepository,
        [FromKeyedServices(FileSearchType.Images)]
        IEnumerable<IFileTypeIdentifier> fileTypeIdentifiers,
        IHashGenerator hashGenerator, IThumbnailGeneratorResolver thumbnailGeneratorResolver,
        [FromKeyedServices(PerceptualHashAlgorithm.QDctHash)]
        IImageHash imageHashGenerator, IHubContext<NotificationHub> notificationContext
    )
    {
        _collectionRepository = collectionRepository;
        _indexingRepository = indexingRepository;
        _imageInfosRepository = imageInfosRepository;
        _similarImagesRepository = similarImagesRepository;
        _imageHashGenerator = imageHashGenerator;
        _notificationContext = notificationContext;
        _imagesIdentifiers = fileTypeIdentifiers.ToArray();
        _hashGenerator = hashGenerator;
        _thumbnailGeneratorResolver = thumbnailGeneratorResolver;
    }

    public async Task<IEnumerable<IGrouping<byte[], File>>> FindSimilarFilesAsync(string[] hypotheticalDuplicates,
        PerceptualHashAlgorithm? perceptualHashAlgorithm = null,
        decimal? degreeOfSimilarity = null, CancellationToken cancellationToken = default)
    {
        perceptualHashAlgorithm = PerceptualHashAlgorithm.QDctHash;
        // Part 1 : Generate and cache perceptual hash of non-corrupted files
        var progress = Channel.CreateUnboundedPrioritized(new UnboundedPrioritizedChannelOptions<int>
            { SingleReader = true, SingleWriter = false });

        var duplicateImagesGroups =
            new ConcurrentDictionary<byte[], ImagesGroup>(Environment.ProcessorCount, hypotheticalDuplicates.Length,
                HashComparer);

        // Await the end of all tasks or the cancellation by the user
        try
        {
            await _collectionRepository.CreateCollectionAsync(CollectionName, cancellationToken);

            await _indexingRepository.DisableIndexingAsync(CollectionName, cancellationToken);

            await Task.WhenAll(
                SendProgress(progress.Reader, NotificationType.HashGenerationProgress, cancellationToken),
                GeneratePerceptualHashes(hypotheticalDuplicates, duplicateImagesGroups, _imageHashGenerator,
                    progress.Writer, cancellationToken)
            );

            await _indexingRepository.EnableIndexingAsync(CollectionName, cancellationToken);

            do
            {
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            } while (!await _indexingRepository.IsIndexingDoneAsync(CollectionName, cancellationToken));
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            duplicateImagesGroups.Clear();
            return [];
        }


        // Part 2 : Group similar images together
        progress = Channel.CreateUnboundedPrioritized(new UnboundedPrioritizedChannelOptions<int>
            { SingleReader = true, SingleWriter = false });

        var groupingChannel = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
            { SingleReader = true, SingleWriter = false });

        var finalImages = new ConcurrentStack<File>();

        // Await the end of all tasks or the cancellation by the user
        try
        {
            await Task.WhenAll(
                ProcessGroupsForFinalList(groupingChannel.Reader, duplicateImagesGroups, finalImages,
                    cancellationToken),
                SendProgress(progress.Reader, NotificationType.SimilaritySearchProgress, cancellationToken),
                LinkSimilarImagesGroupsToOneAnother(duplicateImagesGroups, perceptualHashAlgorithm.Value,
                    _imageHashGenerator.HashSize, degreeOfSimilarity!.Value,
                    groupingChannel.Writer, progress.Writer, cancellationToken)
            );
        }
        catch (Exception)
        {
            duplicateImagesGroups.Clear();
            return [];
        }

        // Return images grouped by hashes
        return finalImages.GroupBy(file => file.Hash, HashComparer).Where(i => i.Skip(1).Any()).ToList();
    }

    private async Task GeneratePerceptualHashes(string[] hypotheticalDuplicates,
        ConcurrentDictionary<byte[], ImagesGroup> duplicateImagesGroups, IImageHash imageHashGenerator,
        ChannelWriter<int> progressWriter, CancellationToken cancellationToken = default)
    {
        var progress = 0;
        var batch = new List<ImagesGroup>(1000);

        foreach (var hypotheticalDuplicatesBatch in hypotheticalDuplicates.Chunk(1000))
        {
            await Parallel.ForEachAsync(hypotheticalDuplicatesBatch,
                new ParallelOptions
                    { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Environment.ProcessorCount },
                async (filePath, hashingToken) =>
                {
                    try
                    {
                        var fileType = GetFileType(filePath, hashingToken);

                        if (fileType is FileType.Animation)
                            return;

                        if (fileType is not (FileType.FFmpegImage or FileType.MagicScalerImage or FileType.LibRawImage
                            or FileType.LibVipsImage))
                        {
                            await SendError(
                                $"File {filePath} is either of type unknown, corrupted or unsupported",
                                _notificationContext, cancellationToken);
                            return;
                        }

                        using var fileHandle = FileReader.GetFileHandle(filePath, true, true);

                        using var memoryMappedFile = FileReader.GetMemoryMappedFile(fileHandle);

                        var hash = await _hashGenerator.GenerateHash(memoryMappedFile, hashingToken);

                        var createdImagesGroup = CreateGroup(hash, filePath, RandomAccess.GetLength(fileHandle),
                            fileType,
                            fileHandle, duplicateImagesGroups, cancellationToken);

                        if (createdImagesGroup == null)
                            return;

                        var imageInfos = await _imageInfosRepository.GetImageInfos(CollectionName,
                            createdImagesGroup.Id, imageHashGenerator.PerceptualHashAlgorithm);

                        int current;
                        if (imageInfos.HasValue && imageInfos.Value.Length != 0)
                        {
                            createdImagesGroup.ImageHash = imageInfos;
                            createdImagesGroup.IsCorruptedOrUnsupported = false;
                            current = Interlocked.Increment(ref progress);
                            progressWriter.TryWrite(current);
                            return;
                        }

                        var thumbnailGenerator =
                            _thumbnailGeneratorResolver.GetThumbnailGenerator(createdImagesGroup.FileType);

                        using var pixels = MemoryOwner<byte>.Allocate(imageHashGenerator.ColorSpace switch
                        {
                            ColorSpace.Grayscale => imageHashGenerator.ImageSize * sizeof(float),
                            _ => imageHashGenerator.ImageSize * 3 * sizeof(float)
                        });

                        if (thumbnailGenerator.GenerateThumbnail(filePath, imageHashGenerator.Width,
                                imageHashGenerator.Height, MemoryMarshal.Cast<byte, float>(pixels.Span),
                                imageHashGenerator.ColorSpace))
                        {
                            createdImagesGroup.ImageHash =
                                imageHashGenerator.GenerateHash(MemoryMarshal.Cast<byte, float>(pixels.Span));
                            if (createdImagesGroup.ImageHash.HasValue && createdImagesGroup.ImageHash.Value.Length != 0)
                                batch.Add(createdImagesGroup);
                            else
                                Console.WriteLine($"Failed to generate thumbnail for {filePath}");
                        }

                        current = Interlocked.Increment(ref progress);
                        progressWriter.TryWrite(current);
                    }
                    catch (IOException)
                    {
                        await SendError($"File {filePath} is being used by another application",
                            _notificationContext, hashingToken);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        throw;
                    }
                });

            if (batch.Count == 0)
                continue;

            await _imageInfosRepository.InsertImageInfos(CollectionName, batch,
                imageHashGenerator.PerceptualHashAlgorithm);

            batch.Clear();
        }

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
            fileType = _imagesIdentifiers[index].GetFileType(hypotheticalDuplicate);
            index++;
        } while (fileType is FileType.CorruptUnknownOrUnsupported &&
                 index < _imagesIdentifiers.Length);

        return fileType;
    }

    private static ImagesGroup? CreateGroup(byte[] id, string path, long length, FileType fileType,
        SafeFileHandle fileHandle, ConcurrentDictionary<byte[], ImagesGroup> duplicateImagesGroups,
        CancellationToken cancellationToken = default)
    {
        var isFirst = duplicateImagesGroups.TryAdd(id, new ImagesGroup());
        var imagesGroup = duplicateImagesGroups[id];

        if (!isFirst)
            return null;

        imagesGroup.Duplicates.Push(path);
        imagesGroup.Id = id;
        imagesGroup.Size = length;
        imagesGroup.FileType = fileType;
        imagesGroup.DateModified = System.IO.File.GetLastWriteTime(fileHandle);
        return imagesGroup;
    }

    private static Task SendError(string message, IHubContext<NotificationHub> notificationContext,
        CancellationToken cancellationToken)
    {
        return notificationContext.Clients.All.SendAsync("notify",
            new Notification(NotificationType.Exception, message),
            cancellationToken);
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

            await _notificationContext.Clients.All.SendAsync("notify",
                new Notification(notificationType, progress.ToString()), cancellationToken);
        }

        await _notificationContext.Clients.All.SendAsync("notify",
            new Notification(notificationType, progress.ToString()), cancellationToken);
    }

    private async Task LinkSimilarImagesGroupsToOneAnother(
        ConcurrentDictionary<byte[], ImagesGroup> duplicateImagesGroups,
        PerceptualHashAlgorithm perceptualHashAlgorithm, int hashSize, decimal degreeOfSimilarity,
        ChannelWriter<byte[]> groupingChannelWriter, ChannelWriter<int> progressWriter,
        CancellationToken cancellationToken)
    {
        var progress = 0;

        var keys = duplicateImagesGroups.Keys.ToArray();

        await Parallel.ForEachAsync(keys,
            new ParallelOptions { CancellationToken = cancellationToken },
            async (key, similarityToken) =>
            {
                try
                {
                    var imagesGroup = duplicateImagesGroups[key];

                    // Get cached similar images
                    imagesGroup.Similarities =
                        await _similarImagesRepository.GetExistingSimilaritiesForImage(CollectionName, imagesGroup.Id,
                            perceptualHashAlgorithm) ?? new ObservableDictionary<byte[], Similarity>(HashComparer);

                    // Check for new similar images excluding the ones cached in a previous search and add to cached ones
                    imagesGroup.Similarities.AddRange(new Dictionary<byte[], Similarity>(
                        await _similarImagesRepository.GetSimilarImages(CollectionName, imagesGroup.Id,
                            imagesGroup.ImageHash!.Value, perceptualHashAlgorithm, degreeOfSimilarity,
                            imagesGroup.Similarities.Keys), HashComparer));

                    // If there were new similar images, associate them to the imagesGroup
                    await _similarImagesRepository.LinkToSimilarImagesAsync(CollectionName, imagesGroup.Id,
                        perceptualHashAlgorithm, imagesGroup.Similarities.Values);

                    imagesGroup.Similarities =
                        new ObservableDictionary<byte[], Similarity>(
                            imagesGroup.Similarities
                                .Where(pair => pair.Value.Distance <= degreeOfSimilarity)
                                .ToDictionary(HashComparer), HashComparer);

                    // Send progress
                    var current = Interlocked.Increment(ref progress);
                    await progressWriter.WriteAsync(current, similarityToken);

                    // Queue to the next step
                    await groupingChannelWriter.WriteAsync(key, similarityToken);
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

        var groupsDone = new HashSet<byte[]>(HashComparer);

        await foreach (var groupId in groupingChannelReader.ReadAllAsync(
                           cancellationToken))
        {
            try
            {
                var imagesGroup = duplicateImagesGroups[groupId];

                // Set the images imagesGroup for removal if its list of similar images is empty
                SetCollectionChangedActionToDeleteGroupIfSimilarImagesEmpty(duplicateImagesGroups, imagesGroup);

                // Removes the groups already done in the current imagesGroup's similar images groups. If the similar images are
                // empty we stop here, else we add back the current imagesGroup's id in case it was among those deleted
                foreach (var i in groupsDone)
                {
                    imagesGroup.Similarities.Remove(i);
                }

                if (imagesGroup.Similarities.Count == 0)
                {
                    continue;
                }

                imagesGroup.Similarities.TryAdd(imagesGroup.Id,
                    new Similarity
                        { OriginalId = imagesGroup.Id, DuplicateId = imagesGroup.Id, Distance = 0 });

                // Here an image was either never processed or has remaining similar images groups. If the remaining groups
                // are only itself and there is only one duplicate we stop here
                if (imagesGroup.Similarities.Count == 1 && imagesGroup.Duplicates.Count == 1)
                {
                    imagesGroup.Similarities.Clear();
                    continue;
                }

                // Here there are either multiple images imagesGroup remaining or it is a single image with multiple duplicates
                // We associate them to one another.
                LinkImagesToParentGroup(groupId, duplicateImagesGroups, finalImages, cancellationToken);

                // After associating the current image with its remaining similar images, we add them to the list of already
                // processed images
                foreach (var id in imagesGroup.Similarities.Keys)
                {
                    groupsDone.Add(id);
                }

                imagesGroup.Similarities.Clear();

                progress++;

                if (progress % 100 != 0)
                    continue;

                await _notificationContext.Clients.All.SendAsync("notify",
                    new Notification(NotificationType.TotalProgress, progress.ToString()),
                    cancellationToken);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        await _notificationContext.Clients.All.SendAsync("notify",
            new Notification(NotificationType.TotalProgress, progress.ToString()),
            cancellationToken);
    }

    private void SetCollectionChangedActionToDeleteGroupIfSimilarImagesEmpty(
        ConcurrentDictionary<byte[], ImagesGroup> duplicateImagesGroups, ImagesGroup imagesGroup)
    {
        imagesGroup.Similarities.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName != nameof(ObservableDictionary<byte[], Similarity>.Count) ||
                imagesGroup.Similarities.Count != 0)
                return;

            if (!duplicateImagesGroups.Remove(imagesGroup.Id, out _))
                Console.WriteLine($"Removal failed for {imagesGroup.Id}");

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
        var parentGroup = duplicateImagesGroups[parentGroupId];
        Parallel.ForEach(parentGroup.Similarities.Keys, new ParallelOptions { CancellationToken = cancellationToken },
            imageGroupId =>
            {
                if (imageGroupId.AsSpan().SequenceEqual(parentGroupId))
                {
                    while (parentGroup.Duplicates.TryPop(out var image))
                    {
                        finalImages.Push(new File
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
                    if (!duplicateImagesGroups.TryGetValue(imageGroupId, out var imagesGroup))
                        return;

                    foreach (var image in imagesGroup.Duplicates)
                    {
                        finalImages.Push(new File
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