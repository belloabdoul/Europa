using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Api.DatabaseRepository.Interfaces;
using Api.Implementations.Common;
using Core.Entities;
using Core.Interfaces;
using Core.Interfaces.Common;
using DotNext.Runtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Win32.SafeHandles;
using File = Core.Entities.File;
using NotificationType = Core.Entities.NotificationType;

namespace Api.Implementations.SimilarImages;

public class SimilarImageFinder : ISimilarFilesFinder
{
    private readonly IDbHelpers _dbHelpers;
    private readonly IHashGenerator _hashGenerator;
    private readonly Dictionary<PerceptualHashAlgorithm, IImageHash> _imageHashGenerators;
    private readonly List<IFileTypeIdentifier> _imagesIdentifiers;
    private readonly IHubContext<NotificationHub> _notificationContext;
    private readonly Dictionary<FileType, IThumbnailGenerator> _thumbnailGenerators;
    private static readonly HashComparer HashComparer = new();

    public SimilarImageFinder(IHubContext<NotificationHub> notificationContext,
        IEnumerable<IFileTypeIdentifier> fileTypeIdentifiers, IHashGenerator hashGenerator,
        IEnumerable<IThumbnailGenerator> thumbnailGenerators, IEnumerable<IImageHash> imageHashGenerators,
        IDbHelpers dbHelpers)
    {
        _notificationContext = notificationContext;
        _imagesIdentifiers = fileTypeIdentifiers.Where(fileTypeIdentifier =>
            fileTypeIdentifier.AssociatedSearchType == FileSearchType.Images).ToList();
        _hashGenerator = hashGenerator;
        _thumbnailGenerators = thumbnailGenerators.ToDictionary(
            thumbnailGenerator => thumbnailGenerator.AssociatedImageType,
            thumbnailGenerator => thumbnailGenerator
        );
        _imageHashGenerators = imageHashGenerators.ToDictionary(
            imageHashGenerator => imageHashGenerator.PerceptualHashAlgorithm,
            imageHashGenerator => imageHashGenerator);
        _dbHelpers = dbHelpers;
    }

    public async Task<IEnumerable<IGrouping<byte[], File>>> FindSimilarFilesAsync(string[] hypotheticalDuplicates,
        PerceptualHashAlgorithm? perceptualHashAlgorithm = null,
        int? degreeOfSimilarity = null, CancellationToken cancellationToken = default)
    {
        // Part 1 : Generate and cache perceptual hash of non-corrupted files
        var progress = Channel.CreateUnboundedPrioritized(new UnboundedPrioritizedChannelOptions<int>
            { SingleReader = true, SingleWriter = false });
        
        var duplicateImagesGroups =
            new ConcurrentDictionary<byte[], ImagesGroup>(Environment.ProcessorCount, hypotheticalDuplicates.Length,
                HashComparer);

        // Await the end of all tasks or the cancellation by the user
        try
        {
            await _dbHelpers.DisableIndexing(cancellationToken);

            await Task.WhenAll(
                SendProgress(progress.Reader, NotificationType.HashGenerationProgress, cancellationToken),
                GeneratePerceptualHashes(hypotheticalDuplicates, duplicateImagesGroups,
                    _imageHashGenerators[perceptualHashAlgorithm!.Value], progress.Writer,
                    cancellationToken)
            );
            
            await _dbHelpers.EnableIndexing(cancellationToken);
        }
        catch (Exception)
        {
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
                    _imageHashGenerators[perceptualHashAlgorithm!.Value].HashSize, degreeOfSimilarity!.Value,
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
        // return [];
    }

    private async Task GeneratePerceptualHashes(string[] hypotheticalDuplicates,
        ConcurrentDictionary<byte[], ImagesGroup> duplicateImagesGroups, IImageHash imageHashGenerator,
        ChannelWriter<int> progressWriter, CancellationToken cancellationToken = default)
    {
        var progress = 0;
        await Parallel.ForAsync<nuint>(0, hypotheticalDuplicates.GetLength(),
            new ParallelOptions
                { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Environment.ProcessorCount },
            async (i, hashingToken) =>
            {
                var filePath = hypotheticalDuplicates[i];
                try
                {
                    var fileType = GetFileType(filePath, hashingToken);

                    if (fileType is FileType.Animation)
                        return;

                    if (fileType is not (FileType.MagicScalerImage or FileType.LibRawImage or FileType.LibVipsImage))
                    {
                        await SendError(
                            $"File {filePath} is either of type unknown, corrupted or unsupported",
                            _notificationContext, cancellationToken);
                        return;
                    }

                    using var fileHandle = FileReader.GetFileHandle(filePath, true, true);

                    var length = RandomAccess.GetLength(fileHandle);

                    var hash = await _hashGenerator.GenerateHash(fileHandle, length, hashingToken);

                    if (hash == null)
                    {
                        _ = SendError(
                            $"File {filePath} is either of type unknown, corrupted or unsupported",
                            _notificationContext, cancellationToken);
                        return;
                    }

                    var createdImagesGroup = CreateGroup(hash, filePath, length, fileType,
                        fileHandle, duplicateImagesGroups, cancellationToken);

                    if (createdImagesGroup == null)
                        return;

                    var imageInfos = await _dbHelpers.GetImageInfos(createdImagesGroup.Id,
                        imageHashGenerator.PerceptualHashAlgorithm);

                    int current;
                    if (imageInfos.ImageHash != null)
                    {
                        createdImagesGroup.ImageHash = imageInfos.ImageHash;
                        createdImagesGroup.IsCorruptedOrUnsupported = false;
                        current = Interlocked.Increment(ref progress);
                        progressWriter.TryWrite(current);
                        return;
                    }

                    createdImagesGroup.ImageHash = await imageHashGenerator.GenerateHash(filePath,
                        _thumbnailGenerators[createdImagesGroup.FileType]);

                    if (createdImagesGroup.ImageHash == null)
                        createdImagesGroup.IsCorruptedOrUnsupported = true;

                    if (createdImagesGroup.IsCorruptedOrUnsupported)
                    {
                        Console.WriteLine(createdImagesGroup.Duplicates.First());
                        return;
                    }

                    if(!imageInfos.Uuid.HasValue)
                        await _dbHelpers.InsertImageInfos(createdImagesGroup, imageHashGenerator.PerceptualHashAlgorithm);
                    else 
                        await _dbHelpers.AddImageHash(imageInfos.Uuid.Value, createdImagesGroup, imageHashGenerator.PerceptualHashAlgorithm);
                    current = Interlocked.Increment(ref progress);
                    progressWriter.TryWrite(current);
                }
                catch (IOException)
                {
                    await SendError($"File {filePath} is being used by another application",
                        _notificationContext, hashingToken);
                }
                catch (Exception)
                {
                    Console.WriteLine(filePath);
                }
            });

        progressWriter.Complete();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
                 index < _imagesIdentifiers.Count);

        return fileType;
    }

    private static ImagesGroup? CreateGroup(byte[] id, string path, long length, FileType fileType,
        SafeFileHandle fileHandle, ConcurrentDictionary<byte[], ImagesGroup> duplicateImagesGroups,
        CancellationToken cancellationToken = default)
    {
        var isFirst = duplicateImagesGroups.TryAdd(id, new ImagesGroup());
        var imagesGroup = duplicateImagesGroups[id];
        imagesGroup.Duplicates.Push(path);

        if (!isFirst)
            return null;

        imagesGroup.Id = id;
        imagesGroup.Size = length;
        imagesGroup.FileType = fileType;
        imagesGroup.DateModified = System.IO.File.GetLastWriteTime(fileHandle);
        return imagesGroup;
    }

    public static Task SendError(string message, IHubContext<NotificationHub> notificationContext,
        CancellationToken cancellationToken)
    {
        return notificationContext.Clients.All.SendAsync("notify",
            new Notification(NotificationType.Exception, message),
            cancellationToken);
    }

    public async Task SendProgress(ChannelReader<int> progressReader, NotificationType notificationType,
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
        PerceptualHashAlgorithm perceptualHashAlgorithm, int hashSize, int degreeOfSimilarity,
        ChannelWriter<byte[]> groupingChannelWriter, ChannelWriter<int> progressWriter,
        CancellationToken cancellationToken)
    {
        var progress = 0;

        var keys = duplicateImagesGroups.Keys.ToArray();


        await Parallel.ForAsync<nuint>(0, keys.GetLength(),
            new ParallelOptions { CancellationToken = cancellationToken },
            async (i, similarityToken) =>
            {
                try
                {
                    var key = keys[i];
                    var imagesGroup = duplicateImagesGroups[key];

                    // Get cached similar images
                    imagesGroup.SimilarImages =
                        await _dbHelpers.GetSimilarImagesAlreadyDoneInRange(imagesGroup.Id, perceptualHashAlgorithm) ??
                        new ObservableHashSet<byte[]>(HashComparer);

                    // Check for new similar images excluding the ones cached in a previous search and add to cached ones
                    imagesGroup.Similarities = await _dbHelpers.GetSimilarImages(imagesGroup.Id, imagesGroup.ImageHash!,
                        perceptualHashAlgorithm, hashSize, degreeOfSimilarity, imagesGroup.SimilarImages);
                    
                    foreach (var similarity in imagesGroup.Similarities)
                        imagesGroup.SimilarImages.Add(similarity.DuplicateId);

                    // If there were new similar images, associate them to the imagesGroup
                    if (imagesGroup.Similarities.Length > 0)
                        await _dbHelpers.LinkToSimilarImagesAsync(imagesGroup.Id, perceptualHashAlgorithm,
                            imagesGroup.Similarities);

                    // Send progress
                    var current = Interlocked.Increment(ref progress);
                    await progressWriter.WriteAsync(current, similarityToken);

                    // Queue to the next step
                    await groupingChannelWriter.WriteAsync(key, similarityToken);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    throw;
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
            var imagesGroup = duplicateImagesGroups[groupId];

            // Set the images imagesGroup for removal if its list of similar images is empty
            SetCollectionChangedActionToDeleteGroupIfSimilarImagesEmpty(duplicateImagesGroups, imagesGroup);

            // Removes the groups already done in the current imagesGroup's similar images groups. If the similar images are
            // empty we stop here, else we add back the current imagesGroup's id in case it was among those deleted
            imagesGroup.SimilarImages!.RemoveWhere(image => groupsDone.Contains(image));

            if (imagesGroup.SimilarImages.Count == 0)
            {
                continue;
            }

            imagesGroup.SimilarImages.Add(imagesGroup.Id);

            // Here an image was either never processed or has remaining similar images groups. If the remaining groups
            // are only itself and there is only one duplicate we stop here
            if (imagesGroup.SimilarImages.Count == 1 && imagesGroup.Duplicates.Count == 1)
            {
                imagesGroup.SimilarImages.Clear();
                continue;
            }

            // Here there are either multiple images imagesGroup remaining or it is a single image with multiple duplicates
            // We associate them to one another.
            LinkImagesToParentGroup(groupId, duplicateImagesGroups, finalImages, cancellationToken);

            // After associating the current image with its remaining similar images, we add them to the list of already
            // processed images
            foreach (var similarImage in imagesGroup.SimilarImages)
                groupsDone.Add(similarImage);

            imagesGroup.SimilarImages.Clear();

            progress++;

            if (progress % 100 != 0)
                continue;

            await _notificationContext.Clients.All.SendAsync("notify",
                new Notification(NotificationType.TotalProgress, progress.ToString()),
                cancellationToken);
        }

        await _notificationContext.Clients.All.SendAsync("notify",
            new Notification(NotificationType.TotalProgress, progress.ToString()),
            cancellationToken);
    }

    private void SetCollectionChangedActionToDeleteGroupIfSimilarImagesEmpty(
        ConcurrentDictionary<byte[], ImagesGroup> duplicateImagesGroups, ImagesGroup imagesGroup)
    {
        imagesGroup.SimilarImages!.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName != nameof(ObservableHashSet<byte[]>.Count) || imagesGroup.SimilarImages.Count != 0)
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
        Parallel.ForEach(parentGroup.SimilarImages!,
            new ParallelOptions
                { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Environment.ProcessorCount },
            imageGroupId =>
            {
                if (imageGroupId == parentGroupId)
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