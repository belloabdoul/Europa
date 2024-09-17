using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Api.DatabaseRepository.Interfaces;
using Api.Implementations.Common;
using Api.Implementations.SimilarImages.ImageHashGenerators;
using CommunityToolkit.HighPerformance.Buffers;
using Core.Entities;
using Core.Interfaces;
using Core.Interfaces.Common;
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
    private readonly IImageHash _imageHashGenerator;
    private readonly List<IFileTypeIdentifier> _imagesIdentifiers;
    private readonly IHubContext<NotificationHub> _notificationContext;
    private readonly List<IThumbnailGenerator> _thumbnailGenerators;

    public PerceptualHashAlgorithm PerceptualHashAlgorithm { get; set; }
    public int DegreeOfSimilarity { get; set; }

    public SimilarImageFinder(IHubContext<NotificationHub> notificationContext,
        IEnumerable<IFileTypeIdentifier> imagesIdentifiers, IHashGenerator hashGenerator,
        IEnumerable<IThumbnailGenerator> thumbnailGenerators, IImageHash imageHashGenerator, IDbHelpers dbHelpers)
    {
        _notificationContext = notificationContext;
        _imagesIdentifiers = imagesIdentifiers.ToList();
        _hashGenerator = hashGenerator;
        _thumbnailGenerators = thumbnailGenerators.ToList();
        _imageHashGenerator = imageHashGenerator;
        _dbHelpers = dbHelpers;
    }

    public async Task<IEnumerable<IGrouping<string, File>>> FindSimilarFilesAsync(
        string[] hypotheticalDuplicates, CancellationToken cancellationToken)
    {
        // Part 1 : Generate and cache perceptual hash of non-corrupted files
        var progress = Channel.CreateUnboundedPrioritized(new UnboundedPrioritizedChannelOptions<int>
            { SingleReader = true, SingleWriter = false });

        var duplicateImagesGroups =
            new ConcurrentDictionary<string, ImagesGroup>(Environment.ProcessorCount, hypotheticalDuplicates.Length);

        // Await the end of all tasks or the cancellation by the user
        try
        {
            await Task.WhenAll(
                SendProgress(progress.Reader, NotificationType.HashGenerationProgress, cancellationToken),
                GeneratePerceptualHashes(hypotheticalDuplicates, duplicateImagesGroups,
                    progress.Writer, cancellationToken)
            );
        }
        catch (Exception)
        {
            StringPool.Shared.Reset();
            duplicateImagesGroups.Clear();
            return [];
        }

        // Part 2 : Group similar images together
        progress = Channel.CreateUnboundedPrioritized(new UnboundedPrioritizedChannelOptions<int>
            { SingleReader = true, SingleWriter = false });

        var groupingChannel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
            { SingleReader = true, SingleWriter = false });

        var finalImages = new ConcurrentStack<File>();

        // Await the end of all tasks or the cancellation by the user
        try
        {
            await Task.WhenAll(
                ProcessGroupsForFinalList(groupingChannel.Reader, duplicateImagesGroups, finalImages,
                    cancellationToken),
                SendProgress(progress.Reader, NotificationType.SimilaritySearchProgress, cancellationToken),
                LinkSimilarImagesGroupsToOneAnother(duplicateImagesGroups, DegreeOfSimilarity,
                    groupingChannel.Writer, progress.Writer, cancellationToken)
            );
        }
        catch (Exception)
        {
            StringPool.Shared.Reset();
            duplicateImagesGroups.Clear();
            return [];
        }

        Console.WriteLine(duplicateImagesGroups.Count);
        // Return images grouped by hashes
        var groups = finalImages.GroupBy(file => file.Hash)
            .Where(i => i.Count() != 1).ToList();
        return groups;
    }

    private async Task GeneratePerceptualHashes(string[] hypotheticalDuplicates,
        ConcurrentDictionary<string, ImagesGroup> duplicateImagesGroups, ChannelWriter<int> progressWriter,
        CancellationToken cancellationToken = default)
    {
        var progress = 0;
        await Parallel.ForAsync(0, hypotheticalDuplicates.Length,
            new ParallelOptions
                { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Environment.ProcessorCount },
            async (i, hashingToken) =>
            {
                try
                {
                    var fileType = GetFileType(hypotheticalDuplicates[i], hashingToken);

                    if (fileType is not (FileType.MagicScalerImage or FileType.LibRawImage or FileType.LibVipsImage))
                    {
                        await SendError(
                            $"File {hypotheticalDuplicates[i]} is either of type unknown, corrupted or unsupported",
                            _notificationContext, cancellationToken);
                        return;
                    }

                    using var fileHandle = FileReader.GetFileHandle(hypotheticalDuplicates[i], true);

                    var length = RandomAccess.GetLength(fileHandle);

                    var hash = _hashGenerator.GenerateHash(fileHandle, length, hashingToken);

                    if (string.IsNullOrEmpty(hash))
                    {
                        await SendError(
                            $"File {hypotheticalDuplicates[i]} is either of type unknown, corrupted or unsupported",
                            _notificationContext, cancellationToken);
                        return;
                    }

                    var createdImagesGroup = CreateGroup(hash, hypotheticalDuplicates[i], length, fileType, fileHandle,
                        duplicateImagesGroups, cancellationToken);

                    if (createdImagesGroup == null)
                        return;

                    var imageHash = await _dbHelpers.GetImageInfosAsync(createdImagesGroup.Id);

                    if (imageHash != null)
                    {
                        createdImagesGroup.ImageHash = imageHash;
                        createdImagesGroup.IsCorruptedOrUnsupported = false;
                    }
                    else
                    {
                        createdImagesGroup.IsCorruptedOrUnsupported = !GeneratePerceptualHash(createdImagesGroup,
                            PerceptualHashAlgorithm, hashingToken);

                        if (createdImagesGroup.ImageHash == null)
                            Console.WriteLine(createdImagesGroup.Duplicates.First());
                        else
                        {
                            await _dbHelpers.CacheHashAsync(createdImagesGroup);
                        }
                    }

                    if (!createdImagesGroup.IsCorruptedOrUnsupported)
                    {
                        var current = Interlocked.Increment(ref progress);
                        await progressWriter.WriteAsync(current, hashingToken);
                    }
                }
                catch (IOException)
                {
                    await SendError($"File {hypotheticalDuplicates[i]} is being used by another application",
                        _notificationContext, hashingToken);
                }
            });

        progressWriter.Complete();
    }

    private FileType GetFileType(string hypotheticalDuplicate, CancellationToken cancellationToken = default)
    {
        // Go through every image identifiers to get the one to use.
        // If the file is not supported send a message, else send the
        // image for the next step which is hash generation and grouping

        FileType? fileType = null;
        var index = 0;
        while (fileType is null or FileType.CorruptUnknownOrUnsupported &&
               index < _imagesIdentifiers.Count)
        {
            fileType = _imagesIdentifiers[index].GetFileType(hypotheticalDuplicate);
            index++;
        }

        return fileType!.Value;
    }

    private static ImagesGroup? CreateGroup(string id, string path, long length, FileType fileType,
        SafeFileHandle fileHandle, ConcurrentDictionary<string, ImagesGroup> duplicateImagesGroups,
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

    [SkipLocalsInit]
    private bool GeneratePerceptualHash(ImagesGroup imagesGroup, PerceptualHashAlgorithm perceptualHashAlgorithm,
        CancellationToken cancellationToken = default)
    {
        // Check if the image was already cached before and only continue if it false
        try
        {
            imagesGroup.Duplicates.TryPeek(out var duplicate);

            var thumbnailGenerator = _thumbnailGenerators.First(service =>
                service.GetType().Name.StartsWith(imagesGroup.FileType.ToString()));

            int width, height;

            // Resize the image with the required dimensions for the perceptual hash
            switch (perceptualHashAlgorithm)
            {
                case PerceptualHashAlgorithm.DifferenceHash:
                    width = DifferenceHash.GetRequiredWidth();
                    height = DifferenceHash.GetRequiredHeight();
                    break;

                case PerceptualHashAlgorithm.PerceptualHash:
                    width = PerceptualHash.GetRequiredWidth();
                    height = PerceptualHash.GetRequiredHeight();
                    break;

                case PerceptualHashAlgorithm.BlockMeanHash:
                default:
                    width = BlockMeanHash.GetRequiredWidth();
                    height = BlockMeanHash.GetRequiredHeight();
                    break;
            }

            Span<byte> pixels = stackalloc byte[width * height];

            // If the image is properly resized there is no reason for the rest to fail
            if (!thumbnailGenerator.GenerateThumbnail(duplicate!, width, height, pixels))
                return false;

            imagesGroup.ImageHash = _imageHashGenerator.GenerateHash(pixels);

            return true;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
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

    public async Task SendProgress(ChannelReader<int> progressReader, NotificationType notificationType,
        CancellationToken cancellationToken)
    {
        await foreach (var hashProcessed in progressReader.ReadAllAsync(cancellationToken))
        {
            var isNextAvailable = await progressReader.WaitToReadAsync(cancellationToken);

            if (isNextAvailable && hashProcessed % 100 != 0)
                continue;

            await _notificationContext.Clients.All.SendAsync("notify",
                new Notification(notificationType, hashProcessed.ToString()), cancellationToken);
        }
    }

    private async Task LinkSimilarImagesGroupsToOneAnother(
        ConcurrentDictionary<string, ImagesGroup> duplicateImagesGroups,
        int degreeOfSimilarity, ChannelWriter<string> groupingChannelWriter,
        ChannelWriter<int> progressWriter, CancellationToken cancellationToken)
    {
        var progress = 0;

        var keys = duplicateImagesGroups.Keys.ToArray();

        await Parallel.ForEachAsync(keys, new ParallelOptions { CancellationToken = cancellationToken },
            async (key, similarityToken) =>
            {
                try
                {
                    var imagesGroup = duplicateImagesGroups[key];

                    // Get cached similar images
                    imagesGroup.SimilarImages = await _dbHelpers.GetSimilarImagesAlreadyDoneInRange(imagesGroup.Id);

                    // Check for new similar images excluding the ones cached in a previous search and add to cached ones
                    imagesGroup.Similarities = await _dbHelpers.GetSimilarImages(imagesGroup.Id, imagesGroup.ImageHash!,
                        degreeOfSimilarity,
                        imagesGroup.SimilarImages);

                    foreach (var similarity in imagesGroup.Similarities)
                        imagesGroup.SimilarImages.Add(similarity.DuplicateId);

                    // If there were new similar images, associate them to the imagesGroup
                    if (imagesGroup.Similarities.Count > 0)
                        await _dbHelpers.LinkToSimilarImagesAsync(imagesGroup.Id, imagesGroup.Similarities);

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

    private async Task ProcessGroupsForFinalList(ChannelReader<string> groupingChannelReader,
        ConcurrentDictionary<string, ImagesGroup> duplicateImagesGroups, ConcurrentStack<File> finalImages,
        CancellationToken cancellationToken)
    {
        try
        {
            var progress = 0;

            var groupsDone = new HashSet<string>();

            await foreach (var groupId in groupingChannelReader.ReadAllAsync(
                               cancellationToken))
            {
                var imagesGroup = duplicateImagesGroups[groupId];

                // Set the images imagesGroup for removal if its list of similar images is empty
                SetCollectionChangedActionToDeleteGroupIfSimilarImagesEmpty(duplicateImagesGroups, imagesGroup);

                // Removes the groups already done in the current imagesGroup's similar images groups. If the similar images are
                // empty we stop here, else we add back the current imagesGroup's id in case it was among those deleted
                imagesGroup.SimilarImages.RemoveWhere(image => groupsDone.Contains(image));

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

                await _notificationContext.Clients.All.SendAsync("notify",
                    new Notification(NotificationType.TotalProgress, (++progress).ToString()),
                    cancellationToken);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    private void SetCollectionChangedActionToDeleteGroupIfSimilarImagesEmpty(
        ConcurrentDictionary<string, ImagesGroup> duplicateImagesGroups, ImagesGroup imagesGroup)
    {
        imagesGroup.SimilarImages.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName != nameof(ObservableHashSet<string>.Count) || imagesGroup.SimilarImages.Count != 0)
                return;

            if (!duplicateImagesGroups.Remove(imagesGroup.Id, out _))
                Console.WriteLine($"Removal failed for {imagesGroup.Id}");
        };
    }

    private static void LinkImagesToParentGroup(string parentGroupId,
        ConcurrentDictionary<string, ImagesGroup> duplicateImagesGroups, ConcurrentStack<File> finalImages,
        CancellationToken cancellationToken)
    {
        // Associated the current imagesGroup of images with its similar imagesGroup of images.
        // In the case of the current imagesGroup, it will not be used after so we dequeue
        // each file's path. For the similar images they will not be dequeued here.
        var parentGroup = duplicateImagesGroups[parentGroupId];
        foreach (var imageGroupId in parentGroup.SimilarImages)

            // new ParallelOptions
            //     { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Environment.ProcessorCount },
            // imageGroupId =>
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
        }
    }
}