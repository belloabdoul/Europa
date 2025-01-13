using System.Collections.Concurrent;
using System.Threading.Channels;
using Api.Client.Repositories;
using Api.Implementations.Commons;
using Core.Entities.Audios;
using Core.Entities.Commons;
using Core.Entities.Files;
using Core.Entities.Notifications;
using Core.Entities.SearchParameters;
using Core.Interfaces.Commons;
using Core.Interfaces.SimilarAudios;
using DotNext.Runtime;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Win32.SafeHandles;
using NSwag.Collections;
using File = Core.Entities.Files.File;

namespace Api.Implementations.SimilarAudios;

public class SimilarAudiosFinder(
    [FromKeyedServices(FileSearchType.Audios)]
    ICollectionRepository collectionRepository,
    [FromKeyedServices(FileSearchType.Audios)]
    IIndexingRepository indexingRepository,
    [FromKeyedServices(FileSearchType.Audios)]
    IEnumerable<IFileTypeIdentifier> fileTypeIdentifiers,
    IAudioHashGenerator audioHashGenerator,
    IHashGenerator hashGenerator,
    IAudioInfosRepository audioInfosRepository,
    ISimilarAudiosRepository similarAudiosRepository,
    IHubContext<NotificationHub> notificationContext)
    : ISimilarFilesFinder
{
    private static readonly HashComparer HashComparer = new();
    private const string CollectionName = "Europa-Audios";

    public async Task<IEnumerable<IGrouping<byte[], File>>> FindSimilarFilesAsync(string[] hypotheticalDuplicates,
        PerceptualHashAlgorithm? perceptualHashAlgorithm = null,
        decimal? degreeOfSimilarity = null, CancellationToken cancellationToken = default)
    {
        // Part 1 : Generate and cache perceptual hash of non-corrupted files
        var progressChannel = Channel.CreateUnboundedPrioritized(new UnboundedPrioritizedChannelOptions<int>
            { SingleReader = true, SingleWriter = true });

        var audiosGroupsChannel = Channel.CreateUnbounded<AudiosGroup>(new UnboundedChannelOptions
            { SingleReader = true, SingleWriter = false });

        var duplicateAudiosGroups =
            new ConcurrentDictionary<byte[], AudiosGroup>(Environment.ProcessorCount, hypotheticalDuplicates.Length,
                HashComparer);

        var filesWithFingerprintsCount = new Dictionary<byte[], int>(HashComparer);

        // Await the end of all tasks or the cancellation by the user
        try
        {
            await collectionRepository.CreateCollectionAsync(CollectionName, cancellationToken);
            await indexingRepository.DisableIndexingAsync(CollectionName, cancellationToken);
            await Task.WhenAll(
                SendProgress(progressChannel.Reader, NotificationType.HashGenerationProgress, cancellationToken),
                InsertFingerprints(audiosGroupsChannel.Reader, filesWithFingerprintsCount, progressChannel.Writer,
                    cancellationToken),
                GenerateAudioFingerprints(hypotheticalDuplicates, duplicateAudiosGroups,
                    audiosGroupsChannel.Writer, cancellationToken)
            );
            await indexingRepository.EnableIndexingAsync(CollectionName, cancellationToken);
            do
            {
                await Task.Delay(delay: TimeSpan.FromSeconds(5), cancellationToken: cancellationToken);
            } while (!await indexingRepository.IsIndexingDoneAsync(collectionName: CollectionName,
                         cancellationToken: cancellationToken));
        }
        catch (OperationCanceledException)
        {
            duplicateAudiosGroups.Clear();
            return [];
        }

        // // Part 2 : Group similar images together
        // progressChannel = Channel.CreateUnboundedPrioritized(new UnboundedPrioritizedChannelOptions<int>
        //     { SingleReader = true, SingleWriter = false });
        //
        // var groupingChannel = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
        //     { SingleReader = true, SingleWriter = false });
        //
        // var finalAudios = new ConcurrentStack<File>();
        //
        // const int thresholdVotes = 4;
        // // Gap allowed in s
        // const double gapAllowed = 2.0;
        // degreeOfSimilarity ??= 0.95m;
        //
        // // Await the end of all tasks or the cancellation by the user
        // try
        // {
        //     await Task.WhenAll(
        //         ProcessGroupsForFinalList(groupingChannel.Reader, duplicateAudiosGroups, finalAudios,
        //             cancellationToken),
        //         SendProgress(progressChannel.Reader, NotificationType.SimilaritySearchProgress, cancellationToken),
        //         LinkSimilarAudiosGroupsToOneAnother(duplicateAudiosGroups, filesWithFingerprintsCount,
        //             degreeOfSimilarity.Value, gapAllowed,
        //             thresholdVotes, groupingChannel.Writer, progressChannel.Writer, cancellationToken)
        //     );
        // }
        // catch (Exception e)
        // {
        //     Console.WriteLine(e);
        //     duplicateAudiosGroups.Clear();
        //     return [];
        // }
        //
        // // Return images grouped by hashes
        // return finalAudios.GroupBy(file => file.Hash, HashComparer).Where(i => i.Skip(1).Any()).ToList();
        return [];
    }

    private async Task GenerateAudioFingerprints(string[] hypotheticalDuplicates,
        ConcurrentDictionary<byte[], AudiosGroup> duplicateAudiosGroups, ChannelWriter<AudiosGroup> audiosGroupsWriter,
        CancellationToken cancellationToken)
    {
        await Parallel.ForAsync<nuint>(0, hypotheticalDuplicates.GetLength(), cancellationToken,
            async (index, hashingToken) =>
            {
                var filePath = hypotheticalDuplicates[index];
                byte[] hash = [];
                try
                {
                    var fileType = GetFileType(filePath, hashingToken);

                    if (fileType is not (FileType.Audio or FileType.AudioVideo))
                        return;

                    using var fileHandle = FileReader.GetFileHandle(filePath, true, true);

                    hash = await hashGenerator.GenerateHash(fileHandle, RandomAccess.GetLength(fileHandle),
                        hashingToken);

                    if (hash.Length == 0)
                        return;

                    var createdAudiosGroup = CreateGroup(hash, filePath, fileType, fileHandle,
                        duplicateAudiosGroups, cancellationToken);

                    if (createdAudiosGroup == null)
                        return;

                    var fingerprintsCount = await audioInfosRepository.GetFingerprintsCount(CollectionName,
                        createdAudiosGroup.Id, hashingToken);

                    if (fingerprintsCount > 0)
                    {
                        // Console.WriteLine($"{filePath} {fingerprintsCount}");
                        createdAudiosGroup.ToInsert = false;
                        createdAudiosGroup.FingerprintsCount = fingerprintsCount;
                        await audiosGroupsWriter.WriteAsync(createdAudiosGroup, hashingToken);
                        return;
                    }

                    createdAudiosGroup.Fingerprints = await audioHashGenerator.GenerateAudioHashesAsync(filePath, hash,
                        cancellationToken: hashingToken);

                    if (createdAudiosGroup.Fingerprints.Count != 0)
                    {
                        createdAudiosGroup.ToInsert = true;
                        await audiosGroupsWriter.WriteAsync(createdAudiosGroup, hashingToken);
                    }
                    else
                        duplicateAudiosGroups.Remove(hash, out _);
                }
                catch (OperationCanceledException)
                {
                    // ignored
                }
                catch (Exception e)
                {
                    Console.WriteLine($"{filePath} {e.Message}");
                    duplicateAudiosGroups.Remove(hash, out _);
                }
            });


        audiosGroupsWriter.Complete();
    }

    private async Task InsertFingerprints(ChannelReader<AudiosGroup> audiosGroupsReader,
        Dictionary<byte[], int> filesWithFingerprintsCount, ChannelWriter<int> progressChannelWriter,
        CancellationToken cancellationToken)
    {
        var progress = 0;
        await foreach (var audioGroup in audiosGroupsReader.ReadAllAsync(cancellationToken))
        {
            if (audioGroup.ToInsert)
            {
                filesWithFingerprintsCount.TryAdd(audioGroup.Id, await audioInfosRepository.InsertFingerprintsAsync(
                    CollectionName, audioGroup.Fingerprints!, cancellationToken));
                audioGroup.Fingerprints?.Clear();
                audioGroup.Fingerprints = null;
            }
            else
                filesWithFingerprintsCount.TryAdd(audioGroup.Id, audioGroup.FingerprintsCount);

            await progressChannelWriter.WriteAsync(++progress, cancellationToken);
        }

        progressChannelWriter.Complete();
    }


    private FileType GetFileType(string hypotheticalDuplicate, CancellationToken cancellationToken = default)
    {
        // Go through every image identifiers to get the one to use.
        // If the file is not supported send a message, else send the
        // image for the next step which is hash generation and grouping

        foreach (var fileTypeIdentifier in fileTypeIdentifiers)
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

            // if (progress % 100 != 0)
            //     continue;

            await notificationContext.Clients.All.SendAsync("notify",
                new Notification(notificationType, progress.ToString()), cancellationToken);
        }

        await notificationContext.Clients.All.SendAsync("notify",
            new Notification(notificationType, progress.ToString()), cancellationToken);
    }

    private async Task LinkSimilarAudiosGroupsToOneAnother(
        ConcurrentDictionary<byte[], AudiosGroup> duplicateAudiosGroups,
        Dictionary<byte[], int> filesWithFingerprintsCount, decimal degreeOfSimilarity, double gapAllowed,
        int thresholdVotes, ChannelWriter<byte[]> groupingChannelWriter, ChannelWriter<int> progressWriter,
        CancellationToken cancellationToken)
    {
        var progress = 0;

        var keys = duplicateAudiosGroups.Keys.ToArray();

        await Parallel.ForEachAsync(keys, cancellationToken,
            async (key, similarityToken) =>
                // foreach (var key in keys)
            {
                try
                {
                    var audiosGroup = duplicateAudiosGroups[key];
                    var fingerprints = await
                        audioHashGenerator.GenerateAudioHashesAsync(audiosGroup.Duplicates.First(), audiosGroup.Id,
                            true, similarityToken);

                    audiosGroup.Matches =
                        await similarAudiosRepository.GetExistingMatchesForFileAsync(CollectionName, audiosGroup.Id,
                            similarityToken);

                    audiosGroup.Matches.Add(audiosGroup.Id,
                        new Similarity { OriginalId = audiosGroup.Id, DuplicateId = audiosGroup.Id, Score = 1 });

                    foreach (var match in await similarAudiosRepository
                                 .GetMatchingFingerprintsAsync(CollectionName, fingerprints,
                                     thresholdVotes, gapAllowed, degreeOfSimilarity, audiosGroup.Id,
                                     audiosGroup.Matches.Keys, filesWithFingerprintsCount, similarityToken))
                    {
                        audiosGroup.Matches.TryAdd(match.Key,
                            new Similarity { OriginalId = key, DuplicateId = match.Key, Score = match.Value });
                    }

                    foreach (var match in audiosGroup.Matches)
                    {
                        if (!duplicateAudiosGroups.TryGetValue(match.Key, out var group))
                            continue;
                        Console.WriteLine(
                            $"{audiosGroup.Duplicates.First()} {group.Duplicates.First()} {match.Value.Score}");
                    }

                    // await _similarAudiosRepository.LinkToSimilarFilesAsync(CollectionName, audiosGroup.Id,
                    //     audiosGroup.Matches.Values, cancellationToken);

                    // Send progress
                    var current = Interlocked.Increment(ref progress);
                    await progressWriter.WriteAsync(current, cancellationToken: similarityToken);

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

    private void SetCollectionChangedActionToDeleteGroupIfSimilarImagesEmpty(
        ConcurrentDictionary<byte[], AudiosGroup> duplicateAudiosGroups, AudiosGroup audiosGroup)
    {
        audiosGroup.Matches.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName != nameof(ObservableDictionary<byte[], Similarity>.Count) ||
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