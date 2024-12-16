using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using Api.Client.Repositories;
using Api.Implementations.Commons;
using Core.Entities.Audios;
using Core.Entities.Commons;
using Core.Entities.Files;
using Core.Entities.Images;
using Core.Entities.Notifications;
using Core.Entities.SearchParameters;
using Core.Interfaces.Commons;
using Core.Interfaces.SimilarAudios;
using DotNext.Collections.Generic;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Win32.SafeHandles;
using NSwag.Collections;
using File = Core.Entities.Files.File;

namespace Api.Implementations.SimilarAudios;

public class SimilarAudiosFinder : ISimilarFilesFinder
{
    private readonly ICollectionRepository _collectionRepository;
    private readonly IAudioHashGenerator _audioHashGenerator;
    private readonly IEnumerable<IFileTypeIdentifier> _fileTypeIdentifiers;
    private readonly IHashGenerator _hashGenerator;
    private readonly IAudioInfosRepository _audioInfosRepository;
    private readonly ISimilarAudiosRepository _similarAudiosRepository;
    private readonly IHubContext<NotificationHub> _notificationContext;
    private readonly IAudioInfosGetter _audioInfosGetter;
    private static readonly HashComparer HashComparer = new();
    private const string CollectionName = "Europa-Audios";

    public SimilarAudiosFinder(
        [FromKeyedServices(FileSearchType.Audios)]
        ICollectionRepository collectionRepository,
        [FromKeyedServices(FileSearchType.Audios)]
        IEnumerable<IFileTypeIdentifier> fileTypeIdentifiers,
        IAudioHashGenerator audioHashGenerator,
        IAudioInfosGetter audioInfosGetter,
        IHashGenerator hashGenerator,
        IAudioInfosRepository audioInfosRepository,
        ISimilarAudiosRepository similarAudiosRepository,
        IHubContext<NotificationHub> notificationContext)
    {
        _collectionRepository = collectionRepository;
        _fileTypeIdentifiers = fileTypeIdentifiers;
        _audioHashGenerator = audioHashGenerator;
        _audioInfosGetter = audioInfosGetter;
        _hashGenerator = hashGenerator;
        _notificationContext = notificationContext;
        _audioInfosRepository = audioInfosRepository;
        _similarAudiosRepository = similarAudiosRepository;
    }

    public async Task<IEnumerable<IGrouping<byte[], File>>> FindSimilarFilesAsync(string[] hypotheticalDuplicates,
        PerceptualHashAlgorithm? perceptualHashAlgorithm = null,
        decimal? degreeOfSimilarity = null, CancellationToken cancellationToken = default)
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;

        // Part 1 : Generate and cache perceptual hash of non-corrupted files
        var progress = Channel.CreateUnboundedPrioritized(new UnboundedPrioritizedChannelOptions<int>
            { SingleReader = true, SingleWriter = false });

        var duplicateAudiosGroups =
            new ConcurrentDictionary<byte[], AudiosGroup>(Environment.ProcessorCount, hypotheticalDuplicates.Length,
                HashComparer);

        // Await the end of all tasks or the cancellation by the user
        try
        {
            await _collectionRepository.CreateCollectionAsync(CollectionName, cancellationToken);
            await Task.WhenAll(
                SendProgress(progress.Reader, NotificationType.HashGenerationProgress, cancellationToken),
                GenerateAudioFingerprints(hypotheticalDuplicates, duplicateAudiosGroups,
                    progress.Writer, cancellationToken)
            );
        }
        catch (OperationCanceledException)
        {
            duplicateAudiosGroups.Clear();
            return [];
        }

        // Part 2 : Group similar images together
        progress = Channel.CreateUnboundedPrioritized(new UnboundedPrioritizedChannelOptions<int>
            { SingleReader = true, SingleWriter = false });

        var groupingChannel = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
            { SingleReader = true, SingleWriter = false });

        var finalAudios = new ConcurrentStack<File>();

        const int thresholdVotes = 4;
        // Gap allowed in s
        const float gapAllowed = 2f;
        degreeOfSimilarity ??= 0.9m;

        // Await the end of all tasks or the cancellation by the user
        try
        {
            await Task.WhenAll(
                ProcessGroupsForFinalList(groupingChannel.Reader, duplicateAudiosGroups, finalAudios,
                    cancellationToken),
                SendProgress(progress.Reader, NotificationType.SimilaritySearchProgress, cancellationToken),
                LinkSimilarAudiosGroupsToOneAnother(duplicateAudiosGroups, degreeOfSimilarity.Value, gapAllowed,
                    thresholdVotes,
                    groupingChannel.Writer, progress.Writer, cancellationToken)
            );
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            duplicateAudiosGroups.Clear();
            return [];
        }

        // Return images grouped by hashes
        return finalAudios.GroupBy(file => file.Hash, HashComparer).Where(i => i.Skip(1).Any()).ToList();
        return [];
    }

    private async Task GenerateAudioFingerprints(string[] hypotheticalDuplicates,
        ConcurrentDictionary<byte[], AudiosGroup> duplicateAudiosGroups, ChannelWriter<int> progressWriter,
        CancellationToken cancellationToken)
    {
        var progress = 0;

        await Parallel.ForEachAsync(hypotheticalDuplicates,
            new ParallelOptions
                { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken },
            async (filePath, hashingToken) =>
            {
                try
                {
                    var fileType = GetFileType(filePath, hashingToken);

                    if (fileType is not (FileType.Audio or FileType.AudioVideo))
                        return;

                    using var fileHandle = FileReader.GetFileHandle(filePath, true, true);

                    var fingerprintingConfiguration = _audioHashGenerator.FingerprintingConfiguration;
                    var estimatedNumberOfFingerprints = _audioInfosGetter.EstimateNumberOfFingerprints(filePath,
                        fingerprintingConfiguration.SampleRate, fingerprintingConfiguration.DftSize,
                        fingerprintingConfiguration.Overlap, fingerprintingConfiguration.FingerprintSize,
                        fingerprintingConfiguration.Stride / fingerprintingConfiguration.Overlap);

                    using var memoryMappedFile = FileReader.GetMemoryMappedFile(fileHandle);
                    var hash = await _hashGenerator.GenerateHash(memoryMappedFile, hashingToken);

                    if (hash.Length == 0)
                        return;

                    var createdAudiosGroup = CreateGroup(hash, filePath, fileType, fileHandle,
                        duplicateAudiosGroups, cancellationToken);

                    if (createdAudiosGroup == null)
                        return;

                    int current;
                    var found = await _audioInfosRepository.IsAlreadyInsertedAsync(CollectionName,
                        createdAudiosGroup.Id, estimatedNumberOfFingerprints, hashingToken);

                    if (found)
                    {
                        createdAudiosGroup.IsCorruptedOrUnsupported = false;
                        current = Interlocked.Increment(ref progress);
                        progressWriter.TryWrite(current);
                        createdAudiosGroup.FingerprintsCount = estimatedNumberOfFingerprints;

                        return;
                    }

                    var fingerprints = await _audioHashGenerator.GenerateAudioHashesAsync(filePath, hash,
                        cancellationToken: hashingToken);

                    if (fingerprints.Count == 0)
                        createdAudiosGroup.IsCorruptedOrUnsupported = true;

                    if (!createdAudiosGroup.IsCorruptedOrUnsupported)
                    {
                        await _audioInfosRepository.InsertFingerprintsAsync(CollectionName, fingerprints,
                            cancellationToken);
                        createdAudiosGroup.FingerprintsCount = estimatedNumberOfFingerprints;
                    }
                    else
                        Console.WriteLine(createdAudiosGroup.Duplicates.First());

                    current = Interlocked.Increment(ref progress);
                    progressWriter.TryWrite(current);
                }
                catch (OperationCanceledException)
                {
                    // ignored
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            });


        progressWriter.Complete();
    }

    private FileType GetFileType(string hypotheticalDuplicate, CancellationToken cancellationToken = default)
    {
        // Go through every image identifiers to get the one to use.
        // If the file is not supported send a message, else send the
        // image for the next step which is hash generation and grouping

        foreach (var fileTypeIdentifier in _fileTypeIdentifiers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileType = fileTypeIdentifier.GetFileType(hypotheticalDuplicate);
            if (fileType != FileType.CorruptUnknownOrUnsupported)
                return fileType;
        }

        return FileType.CorruptUnknownOrUnsupported;
    }

    private static AudiosGroup? CreateGroup(byte[] id, string path, FileType fileType,
        SafeFileHandle fileHandle, ConcurrentDictionary<byte[], AudiosGroup> duplicateAudiosGroups,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var isFirst = duplicateAudiosGroups.TryAdd(id, new AudiosGroup());
        var audiosGroup = duplicateAudiosGroups[id];
        audiosGroup.Duplicates.Push(path);

        cancellationToken.ThrowIfCancellationRequested();
        if (!isFirst)
            return null;

        audiosGroup.Id = id;
        audiosGroup.Size = RandomAccess.GetLength(fileHandle);
        audiosGroup.FileType = fileType;
        audiosGroup.DateModified = System.IO.File.GetLastWriteTime(fileHandle);
        cancellationToken.ThrowIfCancellationRequested();
        return audiosGroup;
    }

    public static Task SendError(string message, IHubContext<NotificationHub> notificationContext,
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

    private async Task LinkSimilarAudiosGroupsToOneAnother(
        ConcurrentDictionary<byte[], AudiosGroup> duplicateAudiosGroups, decimal degreeOfSimilarity, float gapAllowed,
        byte thresholdVotes, ChannelWriter<byte[]> groupingChannelWriter, ChannelWriter<int> progressWriter,
        CancellationToken cancellationToken)
    {
        var progress = 0;

        var keys = duplicateAudiosGroups.Keys.ToArray();

        await Parallel.ForEachAsync(keys,
            new ParallelOptions { CancellationToken = cancellationToken },
            async (key, similarityToken) =>
            {
                try
                {
                    var audiosGroup = duplicateAudiosGroups[key];
                    var fingerprints = await
                        _audioHashGenerator.GenerateAudioHashesAsync(audiosGroup.Duplicates.First(), audiosGroup.Id,
                            true, similarityToken);

                    audiosGroup.Matches =
                        await _similarAudiosRepository.GetExistingMatchesForFileAsync(CollectionName, audiosGroup.Id,
                            cancellationToken);

                    var matchingFingerprints =
                        new Dictionary<byte[], ConcurrentDictionary<double, byte>>(HashComparer);

                    audiosGroup.Matches.TryAdd(
                        audiosGroup.Id,
                        new Similarity { OriginalId = audiosGroup.Id, DuplicateId = audiosGroup.Id, Score = 1 }
                    );

                    foreach (var fingerprint in fingerprints)
                    {
                        foreach (var match in await _similarAudiosRepository
                                     .GetMatchingFingerprintsAsync(CollectionName, fingerprint,
                                         thresholdVotes, gapAllowed, audiosGroup.Matches.Keys, similarityToken))
                        {
                            var matchPositions = matchingFingerprints.GetOrAdd(match.Key,
                                new ConcurrentDictionary<double, byte>());

                            matchPositions.TryAdd(match.Value, Convert.ToByte(1));
                        }
                    }

                    foreach (var match in matchingFingerprints)
                    {
                        if (!duplicateAudiosGroups.TryGetValue(match.Key, out _))
                            continue;

                        var matchScore = decimal.Divide(match.Value.Count, audiosGroup.FingerprintsCount);

                        if (matchScore >= degreeOfSimilarity)
                            audiosGroup.Matches.TryAdd(match.Key,
                                new Similarity { OriginalId = key, DuplicateId = match.Key, Score = matchScore });
                    }

                    await _similarAudiosRepository.LinkToSimilarFilesAsync(CollectionName, audiosGroup.Id,
                        audiosGroup.Matches.Values, cancellationToken);

                    // Send progress
                    var current = Interlocked.Increment(ref progress);
                    await progressWriter.WriteAsync(current, similarityToken);

                    // Queue to the next step
                    await groupingChannelWriter.WriteAsync(key, similarityToken);
                }
                catch (OperationCanceledException)
                {
                    // ignored
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
        ConcurrentDictionary<byte[], AudiosGroup> duplicateAudiosGroups, ConcurrentStack<File> finalAudios,
        CancellationToken cancellationToken)
    {
        var progress = 0;

        var groupsDone = new HashSet<byte[]>(HashComparer);

        await foreach (var groupId in groupingChannelReader.ReadAllAsync(
                           cancellationToken))
        {
            try
            {
                var audiosGroup = duplicateAudiosGroups[groupId];

                // Set the images audiosGroup for removal if its list of similar images is empty
                SetCollectionChangedActionToDeleteGroupIfSimilarImagesEmpty(duplicateAudiosGroups, audiosGroup);

                // Removes the groups already done in the current audiosGroup's similar images groups. If the similar images are
                // empty we stop here, else we add back the current audiosGroup's id in case it was among those deleted
                foreach (var i in groupsDone)
                {
                    audiosGroup.Matches.Remove(i);
                }

                if (audiosGroup.Matches.Count == 0)
                {
                    continue;
                }

                audiosGroup.Matches.TryAdd(audiosGroup.Id,
                    new Similarity { OriginalId = audiosGroup.Id, DuplicateId = audiosGroup.Id, Score = 0 });

                // Here an image was either never processed or has remaining similar images groups. If the remaining groups
                // are only itself and there is only one duplicate we stop here
                if (audiosGroup.Matches.Count == 1 && audiosGroup.Duplicates.Count == 1)
                {
                    audiosGroup.Matches.Clear();
                    continue;
                }

                // Here there are either multiple images audiosGroup remaining or it is a single image with multiple duplicates
                // We associate them to one another.
                LinkImagesToParentGroup(groupId, duplicateAudiosGroups, finalAudios, cancellationToken);

                // After associating the current image with its remaining similar images, we add them to the list of already
                // processed images
                foreach (var id in audiosGroup.Matches.Keys)
                {
                    groupsDone.Add(id);
                }

                audiosGroup.Matches.Clear();

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
        ConcurrentDictionary<byte[], AudiosGroup> duplicateAudiosGroups, AudiosGroup audiosGroup)
    {
        audiosGroup.Matches.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName != nameof(ObservableDictionary<byte[], FingerprintMatch>.Count) ||
                audiosGroup.Matches.Count != 0)
                return;

            if (!duplicateAudiosGroups.Remove(audiosGroup.Id, out _))
                Console.WriteLine($"Removal failed for {audiosGroup.Id}");

            if (duplicateAudiosGroups.Count % 1000 == 0)
                GC.Collect();
        };
    }

    private static void LinkImagesToParentGroup(byte[] parentGroupId,
        ConcurrentDictionary<byte[], AudiosGroup> duplicateAudiosGroups, ConcurrentStack<File> finalAudios,
        CancellationToken cancellationToken)
    {
        // Associated the current audiosGroup of images with its similar audiosGroup of images.
        // In the case of the current audiosGroup, it will not be used after so we dequeue
        // each file's path. For the similar images they will not be dequeued here.
        var parentGroup = duplicateAudiosGroups[parentGroupId];
        Parallel.ForEach(parentGroup.Matches.Keys, new ParallelOptions { CancellationToken = cancellationToken },
            imageGroupId =>
            {
                if (imageGroupId.AsSpan().SequenceEqual(parentGroupId))
                {
                    while (parentGroup.Duplicates.TryPop(out var image))
                    {
                        finalAudios.Push(new File
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
                    if (!duplicateAudiosGroups.TryGetValue(imageGroupId, out var audiosGroup))
                        return;

                    foreach (var image in audiosGroup.Duplicates)
                    {
                        finalAudios.Push(new File
                        {
                            Path = image,
                            Size = audiosGroup.Size,
                            DateModified = audiosGroup.DateModified,
                            Hash = parentGroupId
                        });
                    }
                }
            });
    }
}