using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Threading.Channels;
using Api.DatabaseRepository.Interfaces;
using Api.Implementations.Common;
using Blake3;
using Core.Entities;
using Core.Interfaces;
using Core.Interfaces.Common;
using Microsoft.AspNetCore.SignalR;
using ObservableCollections;
using File = Core.Entities.File;
using ImagesGroup = Core.Entities.ImagesGroup;
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

    public SimilarImageFinder(IHubContext<NotificationHub> notificationContext,
        List<IFileTypeIdentifier> imagesIdentifiers, IHashGenerator hashGenerator,
        List<IThumbnailGenerator> thumbnailGenerators, IImageHash imageHashGenerator, IDbHelpers dbHelpers)
    {
        _notificationContext = notificationContext;
        _imagesIdentifiers = imagesIdentifiers;
        _hashGenerator = hashGenerator;
        _thumbnailGenerators = thumbnailGenerators;
        _imageHashGenerator = imageHashGenerator;
        _dbHelpers = dbHelpers;
    }

    public int DegreeOfSimilarity { get; set; }

    public async Task<IEnumerable<IGrouping<Hash, File>>> FindSimilarFilesAsync(
        string[] hypotheticalDuplicates, CancellationToken cancellationToken)
    {
        var maxDegreeOfParallelism = Environment.ProcessorCount;

        // Part 1 : Generate and cache perceptual hash of non-corrupted files
        var nonCorruptedImages = Channel.CreateBounded<(string Path, FileType FileType)>(
            new BoundedChannelOptions(maxDegreeOfParallelism)
                { SingleReader = false, SingleWriter = false });

        // A channel with multithreaded read for most images
        var smallImagesForPerceptualHashing = Channel.CreateBounded<ImagesGroup>(
            new BoundedChannelOptions(maxDegreeOfParallelism)
                { SingleReader = false, SingleWriter = false });

        // The channel for managing progress
        var progress = Channel.CreateUnbounded<NotificationType>(new UnboundedChannelOptions
            { SingleReader = true, SingleWriter = false });

        // Launch progress task
        var progressTask = SendProgress(progress.Reader, _notificationContext, cancellationToken);

        // Launch perceptual hashing task
        using var perceptualHashGenerationForSmallImagesTask =
            GeneratePerceptualHashes(smallImagesForPerceptualHashing.Reader, progress.Writer, maxDegreeOfParallelism,
                cancellationToken);

        // Launch cryptographic hash and grouping task
        using var duplicatesGroupingTask = GroupDuplicateImages(nonCorruptedImages.Reader,
            smallImagesForPerceptualHashing.Writer, cancellationToken);

        // Launch filtering of non-corrupted or unsupported format task
        using var filteringTask = AllowNonCorruptedImages(hypotheticalDuplicates, nonCorruptedImages.Writer,
            cancellationToken);

        // Await everything until it is done or the user cancel a task
        await Task.WhenAll(filteringTask, duplicatesGroupingTask, perceptualHashGenerationForSmallImagesTask,
            progressTask);

        // If the user cancel, stop and return nothing
        if (filteringTask.IsCanceled || filteringTask.IsCanceled || duplicatesGroupingTask.IsCanceled ||
            perceptualHashGenerationForSmallImagesTask.IsCanceled)
            return [];

        var imagesGroups = duplicatesGroupingTask.GetAwaiter().GetResult();

        // Part 2 : Group similar images together
        progress = Channel.CreateUnbounded<NotificationType>(new UnboundedChannelOptions
            { SingleReader = true, SingleWriter = false });

        var groupingChannel = Channel.CreateUnbounded<Hash>(new UnboundedChannelOptions
            { SingleReader = true, SingleWriter = false });

        var finalImages = new ConcurrentQueue<File>();

        progressTask = SendProgress(progress.Reader, _notificationContext, cancellationToken);

        // Launch task for grouping similar images groups together
        using var groupingTask = ProcessGroupsForFinalList(groupingChannel.Reader, imagesGroups, finalImages,
            cancellationToken);

        // Launch task for finding similar images using redis
        using var similarityTask = LinkSimilarImagesGroupsToOneAnother(imagesGroups, DegreeOfSimilarity,
            groupingChannel.Writer, progress.Writer, cancellationToken);

        // Wait for all tasks to finish or the user's cancellation
        await Task.WhenAll(similarityTask, progressTask, groupingTask);

        // Dispose of the progress task since it is the only one without using
        progressTask.Dispose();

        // Return images grouped by hashes
        var groups = finalImages.GroupBy(file => file.Hash)
            .Where(i => i.Count() != 1).ToList();
        return groups;
    }

    private async Task AllowNonCorruptedImages(string[] hypotheticalDuplicates,
        ChannelWriter<(string Path, FileType FileType)> nonCorruptedImages,
        CancellationToken cancellationToken)
    {
        await Parallel.ForAsync(0, hypotheticalDuplicates.Length,
            new ParallelOptions
                { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Environment.ProcessorCount },
            async (i, corruptionToken) =>
            {
                // Go through every image identifiers to get the one to use.
                // If the file is not supported send a message, else send the
                // image for the next step which is hash generation and grouping
                // If the file is in use in another application do not process it.
                try
                {
                    FileType? fileType = null;
                    var index = 0;
                    while (fileType is null or FileType.CorruptUnknownOrUnsupported &&
                           index < _imagesIdentifiers.Count)
                    {
                        fileType = _imagesIdentifiers[index].GetFileType(hypotheticalDuplicates[i]);
                        index++;
                    }

                    switch (fileType!.Value)
                    {
                        case FileType.CorruptUnknownOrUnsupported:
                            await SendError(
                                $"File {hypotheticalDuplicates[i]} is either of type unknown, corrupted or unsupported",
                                _notificationContext, corruptionToken);
                            break;
                        case FileType.MagicScalerImage or FileType.LibRawImage
                            or FileType.LibVipsImage:
                        {
                            await nonCorruptedImages.WriteAsync(
                                (Path: hypotheticalDuplicates[i], FileType: fileType.Value),
                                corruptionToken);
                            break;
                        }
                    }
                }
                catch (IOException)
                {
                    await SendError($"File {hypotheticalDuplicates[i]} is being used by another application",
                        _notificationContext, corruptionToken);
                }
            });

        nonCorruptedImages.Complete();
    }

    private async Task<ConcurrentDictionary<Hash, ImagesGroup>> GroupDuplicateImages(
        ChannelReader<(string Path, FileType FileType)> nonCorruptedImages,
        ChannelWriter<ImagesGroup> imagesForPerceptualHashing, CancellationToken cancellationToken)
    {
        var copiesGroups =
            new ConcurrentDictionary<Hash, ImagesGroup>();

        // Generate integrity hash and group perfect copies together.
        // If for any reason another application block access to the
        // file, do not process it.
        await Parallel.ForEachAsync(nonCorruptedImages.ReadAllAsync(cancellationToken),
            new ParallelOptions
                { CancellationToken = cancellationToken, MaxDegreeOfParallelism = Environment.ProcessorCount },
            async (hypotheticalDuplicate, hashingToken) =>
            {
                try
                {
                    using var fileHandle = FileReader.GetFileHandle(hypotheticalDuplicate.Path, true);

                    var length = RandomAccess.GetLength(fileHandle);

                    var hash = _hashGenerator.GenerateHash(fileHandle, length, hashingToken);

                    if (!hash.HasValue)
                    {
                        await SendError(
                            $"File {hypotheticalDuplicate} is either of type unknown, corrupted or unsupported",
                            _notificationContext, hashingToken);
                        return;
                    }

                    var isFirst = copiesGroups.TryAdd(hash.Value, new ImagesGroup());
                    var group = copiesGroups[hash.Value];
                    group.Duplicates.Enqueue(hypotheticalDuplicate.Path);

                    if (isFirst)
                    {
                        group.Id = hash.Value;
                        group.Size = length;
                        group.DateModified = System.IO.File.GetLastWriteTime(fileHandle);
                        group.FileType = hypotheticalDuplicate.FileType;

                        await imagesForPerceptualHashing.WriteAsync(group, cancellationToken);
                    }
                    else if (group.IsCorruptedOrUnsupported)
                    {
                        // if the file is not the first, there is the possibility that the file was a corrupt one who was
                        // already removed in the next step and ImagesGroup.IsCorruptedOrUnsupported was set to true.
                        // If so, also send the proper message and remove the current one
                        for (var i = 0; i < group.Duplicates.Count; i++)
                            if (group.Duplicates.TryDequeue(out var duplicate))
                                await SendError(
                                    $"File {duplicate} is either of type unknown, corrupted or unsupported",
                                    _notificationContext, cancellationToken);
                    }
                }
                catch (IOException)
                {
                    await SendError($"File {hypotheticalDuplicate} is being used by another application",
                        _notificationContext, hashingToken);
                }
            });

        imagesForPerceptualHashing.Complete();
        return copiesGroups;
    }

    private async Task GeneratePerceptualHashes(ChannelReader<ImagesGroup> imagesForPerceptualHashing,
        ChannelWriter<NotificationType> progressWriter, int maxDegreeOfParallelism,
        CancellationToken cancellationToken = default)
    {
        // Generate the hash for each 
        await Parallel.ForEachAsync(imagesForPerceptualHashing.ReadAllAsync(cancellationToken),
            new ParallelOptions
                { CancellationToken = cancellationToken, MaxDegreeOfParallelism = maxDegreeOfParallelism },
            async (group, hashingToken) =>
            {
                await GeneratePerceptualHash(group,
                    _thumbnailGenerators.First(service =>
                        service.GetType().Name.StartsWith(group.FileType.ToString())),
                    _imageHashGenerator, _dbHelpers, progressWriter, _notificationContext, hashingToken);
            });

        progressWriter.Complete();
    }

    private static async Task GeneratePerceptualHash(ImagesGroup imagesGroup,
        IThumbnailGenerator thumbnailGenerator,
        IImageHash imageHashGenerator, IDbHelpers dbHelpers, ChannelWriter<NotificationType> progressWriter,
        IHubContext<NotificationHub> notificationContext, CancellationToken cancellationToken)
    {
        // Check if the image was already cached before and only continue if it false
        var imageHash = await dbHelpers.GetImageInfosAsync(imagesGroup.Id);

        if (imageHash != null)
        {
            imagesGroup.ImageHash = imageHash;
            imagesGroup.IsCorruptedOrUnsupported = false;
        }
        else
        {
            try
            {
                imagesGroup.Duplicates.TryPeek(out var duplicate);

                // Resize the image with the required dimensions for the perceptual hash
                var pixels = thumbnailGenerator.GenerateThumbnail(duplicate!, imageHashGenerator.GetRequiredWidth(),
                    imageHashGenerator.GetRequiredHeight());

                // If the image is properly resized there is no reason for the rest to fail
                if (pixels.Length != 0)
                {
                    imagesGroup.ImageHash = imageHashGenerator.GenerateHash(pixels);

                    await dbHelpers.CacheHashAsync(imagesGroup);

                    imagesGroup.IsCorruptedOrUnsupported = false;
                }
                else
                {
                    imagesGroup.IsCorruptedOrUnsupported = true;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                for (var i = 0; i < imagesGroup.Duplicates.Count; i++)
                    if (imagesGroup.Duplicates.TryDequeue(out var duplicate))
                        await SendError($"File {duplicate} is either of type unknown, corrupted or unsupported",
                            notificationContext, cancellationToken);
            }
        }

        // Only send a progress if the image is a valid image already or newly cached
        if (!imagesGroup.IsCorruptedOrUnsupported)
            await progressWriter.WriteAsync(NotificationType.HashGenerationProgress, cancellationToken);
    }

    public static async Task SendError(string message, IHubContext<NotificationHub> notificationContext,
        CancellationToken cancellationToken)
    {
        await notificationContext.Clients.All.SendAsync("notify",
            new Notification(NotificationType.Exception, message),
            cancellationToken);
    }

    public async Task SendProgress(ChannelReader<NotificationType> progressChannelReader,
        IHubContext<NotificationHub> notificationContext, CancellationToken cancellationToken)
    {
        var progress = 0;
        await foreach (var notificationType in progressChannelReader.ReadAllAsync(cancellationToken))
        {
            await notificationContext.Clients.All.SendAsync("notify",
                new Notification(notificationType, (++progress).ToString()), cancellationToken);
            if (progress % 1000 == 0)
                GC.Collect(2, GCCollectionMode.Default, false, true);
        }
    }

    private async Task LinkSimilarImagesGroupsToOneAnother(ConcurrentDictionary<Hash, ImagesGroup> imagesGroups,
        int degreeOfSimilarity, ChannelWriter<Hash> groupingChannelWriter,
        ChannelWriter<NotificationType> progressWriter, CancellationToken cancellationToken)
    {
        var progress = 0;

        var keys = imagesGroups.Keys.ToArray();

        await Parallel.ForEachAsync(keys, new ParallelOptions { CancellationToken = cancellationToken },
            async (key, similarityToken) =>
            {
                var group = imagesGroups[key];

                // Get cached similar images
                group.SimilarImages = await _dbHelpers.GetSimilarImagesAlreadyDoneInRange(group.Id);

                // // Check for new similar images excluding the ones cached in a previous search and add to cached ones
                group.Similarities = await _dbHelpers.GetSimilarImages(group.Id, group.ImageHash!, degreeOfSimilarity,
                    group.SimilarImages);

                foreach (var similarity in group.Similarities)
                    group.SimilarImages.Add(similarity.DuplicateId);
                
                // If there were new similar images, associate them to the group
                if (group.Similarities.Count > 0)
                    await _dbHelpers.LinkToSimilarImagesAsync(group.Id, group.Similarities);

                // Send progress
                var current = Interlocked.Increment(ref progress);

                await progressWriter.WriteAsync(NotificationType.SimilaritySearchProgress, similarityToken);

                // Queue to the next step
                await groupingChannelWriter.WriteAsync(key, similarityToken);

                if (current % 1000 == 0)
                    GC.Collect(2, GCCollectionMode.Default, false, true);
            });

        progressWriter.Complete();
        groupingChannelWriter.Complete();
    }

    private async Task ProcessGroupsForFinalList(ChannelReader<Hash> groupingChannelReader,
        ConcurrentDictionary<Hash, ImagesGroup> imagesGroups, ConcurrentQueue<File> finalImages,
        CancellationToken cancellationToken)
    {
        var progress = 0;

        var groupsDone = new HashSet<Hash>();

        await foreach (var groupId in groupingChannelReader.ReadAllAsync(
                           cancellationToken))
        {
            var group = imagesGroups[groupId];

            // Set the images group for removal if its list of similar images is empty
            SetCollectionChangedActionToDeleteGroupIfSimilarImagesEmpty(imagesGroups, group);

            // Removes the groups already done in the current group's similar images groups. If the similar images are
            // empty we stop here, else we add back the current group's id in case it was among those deleted
            group.SimilarImages.RemoveRange(groupsDone);

            if (group.SimilarImages.Count == 0)
            {
                continue;
            }

            group.SimilarImages.Add(group.Id);

            // Here an image was either never processed or has remaining similar images groups. If the remaining groups
            // are only itself and there is only one duplicate we stop here
            if (group.SimilarImages.Count == 1 && group.Duplicates.Count == 1)
            {
                group.SimilarImages.Clear();
                continue;
            }

            // Here there are either multiple images group remaining or it is a single image with multiple duplicates
            // We associate them to one another.
            LinkImagesToParentGroup(groupId, imagesGroups, finalImages);

            // After associating the current image with its remaining similar images, we add them to the list of already
            // processed images
            foreach (var similarImage in group.SimilarImages)
                groupsDone.Add(similarImage);

            group.SimilarImages.Clear();

            await _notificationContext.Clients.All.SendAsync("notify",
                new Notification(NotificationType.TotalProgress, (++progress).ToString()),
                cancellationToken);
        }
    }

    private void SetCollectionChangedActionToDeleteGroupIfSimilarImagesEmpty(
        ConcurrentDictionary<Hash, ImagesGroup> imagesGroups, ImagesGroup group)
    {
        group.SimilarImages.CollectionChanged += (in NotifyCollectionChangedEventArgs<Hash> args) =>
        {
            if (args.Action != NotifyCollectionChangedAction.Reset &&
                (args.Action != NotifyCollectionChangedAction.Remove || group.SimilarImages.Count != 0))
                return;

            if (!imagesGroups.Remove(group.Id, out _))
                Console.WriteLine($"Removal failed for {group.Id}");
            else if (imagesGroups.Count % 1000 == 0)
                GC.Collect(2, GCCollectionMode.Default, false, true);
        };
    }

    private static void LinkImagesToParentGroup(Hash parentGroupId,
        ConcurrentDictionary<Hash, ImagesGroup> imagesGroups, ConcurrentQueue<File> finalImages)
    {
        // Associated the current group of images with its similar group of images.
        // In the case of the current group, it will not be used after so we dequeue
        // each file's path. For the similar images they will not be dequeued here.
        foreach (var similarImagesGroup in imagesGroups[parentGroupId].SimilarImages)
        {
            if (similarImagesGroup == parentGroupId)
            {
                while (!imagesGroups[parentGroupId].Duplicates.IsEmpty)
                    if (imagesGroups[parentGroupId].Duplicates.TryDequeue(out var image))
                        finalImages.Enqueue(new File
                        {
                            Path = image,
                            Size = imagesGroups[parentGroupId].Size,
                            DateModified = imagesGroups[parentGroupId].DateModified,
                            Hash = imagesGroups[parentGroupId].Id
                        });
            }
            else
            {
                if (!imagesGroups.TryGetValue(similarImagesGroup, out var result))
                    return;

                foreach (var image in result.Duplicates)
                    finalImages.Enqueue(new File
                    {
                        Path = image,
                        Size = result.Size,
                        DateModified = result.DateModified,
                        Hash = parentGroupId
                    });
            }
        }
    }
}