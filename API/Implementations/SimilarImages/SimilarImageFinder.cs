using System.Collections.Concurrent;
using System.Threading.Channels;
using API.Implementations.Common;
using Blake3;
using Core.Entities;
using Core.Interfaces.Common;
using Core.Interfaces.DuplicatesByHash;
using Core.Interfaces.SimilarImages;
using Database.Interfaces;
using Emgu.CV.Util;
using Microsoft.AspNetCore.SignalR;
using File = Core.Entities.File;

namespace API.Implementations.SimilarImages;

public class SimilarImageFinder : ISimilarImagesFinder
{
    // private readonly IDbHelpers _dbHelpers;
    private readonly IFileReader _fileReader;
    private readonly IFileTypeIdentifier _fileTypeIdentifier;

    private readonly IHashGenerator _hashGenerator;

    private readonly IImageHashGenerator _imageHashGenerator;
    private readonly IHubContext<NotificationHub> _notificationContext;

    public SimilarImageFinder(IHubContext<NotificationHub> notificationContext, IFileReader fileReader,
        IFileTypeIdentifier fileTypeIdentifier, IHashGenerator hashGenerator,
        IImageHashGenerator imageHashGenerator/*, IDbHelpers dbHelpers*/)
    {
        _notificationContext = notificationContext;
        _fileReader = fileReader;
        _fileTypeIdentifier = fileTypeIdentifier;
        _hashGenerator = hashGenerator;
        _imageHashGenerator = imageHashGenerator;
        // _dbHelpers = dbHelpers;
    }

    public async Task<IEnumerable<IGrouping<Hash, File>>> FindSimilarImagesAsync(
        HashSet<string> hypotheticalDuplicates, double degreeOfSimilarity,
        CancellationToken cancellationToken)
    {
        // Part 1 : Generate and cache perceptual hash of non-corrupted files
        var progress = Channel.CreateUnbounded<NotificationType>();

        // var progressTask = SendProgress(progress, cancellationToken);
        //
        // var hashGenerationTask = GenerateImageHashForNonCorruptedFiles(hypotheticalDuplicates, progress,
        //     cancellationToken);
        //
        // await Task.WhenAll(await hashGenerationTask, await progressTask);
        //
        // var imagesGroups = hashGenerationTask.Result.Result;

        // Part 2 : Group similar images together
        progress = Channel.CreateUnbounded<NotificationType>();
        
        var groupingChannel = Channel.CreateUnbounded<ImagesGroup>();

        var finalImages = new ConcurrentQueue<File>();

        // var groupingTask =
        //     ProcessGroupsForFinalList(groupingChannel, imagesGroups, finalImages, cancellationToken);
        //
        // progressTask = SendProgress(progress, cancellationToken);
        //
        // var similarityTask = LinkSimilarImagesGroupsToOneAnother(imagesGroups, degreeOfSimilarity,
        //     groupingChannel, progress, cancellationToken);
        //
        // await Task.WhenAll(await similarityTask, await progressTask, await groupingTask);
        //
        // Console.WriteLine(imagesGroups.Count);
        // var groups = finalImages.GroupBy(file => file.Hash)
        //     .Where(i => i.Count() != 1).ToList();
        // return groups;
        return [];
    }

    // private Task<Task<Dictionary<long, ImagesGroup>>> GenerateImageHashForNonCorruptedFiles(
    //     SortedList<string, (long, DateTime)> hypotheticalDuplicates,
    //     Channel<NotificationType, NotificationType> progress,
    //     CancellationToken cancellationToken)
    // {
    //     return Task.Factory.StartNew(async () =>
    //         {
    //             var copiesGroups =
    //                 new ConcurrentDictionary<byte[], ImagesGroup>(-1, hypotheticalDuplicates.Count,
    //                     new ByteArrayEqualityComparer());
    //
    //             // Generate integrity hash and group perfect copies together
    //             await Parallel.ForAsync(0, hypotheticalDuplicates.Count,
    //                 new ParallelOptions
    //                     { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken },
    //                 async (i, hashingToken) =>
    //                 {
    //                     var hypotheticalDuplicate = hypotheticalDuplicates.GetKeyAtIndex(i);
    //                     var type = _fileTypeIdentifier.GetFileType(hypotheticalDuplicate);
    //
    //                     switch (type)
    //                     {
    //                         case FileType.Corrupt:
    //                             _ = _notificationContext.Clients.All.SendAsync("notify",
    //                                 new Notification(NotificationType.Exception,
    //                                     $"File {hypotheticalDuplicate} is corrupted"),
    //                                 cancellationToken: hashingToken);
    //                             break;
    //                         case FileType.GifImage or FileType.Image:
    //                         {
    //                             using var fileHandle = _fileReader.GetFileHandle(hypotheticalDuplicate);
    //
    //                             var length = hypotheticalDuplicates.GetValueAtIndex(i).Item1;
    //
    //                             var hash = await _hashGenerator.GenerateHashAsync(fileHandle, length,
    //                                 cancellationToken: hashingToken);
    //
    //                             var isFirst = copiesGroups.TryAdd(hash, new ImagesGroup());
    //                             var group = copiesGroups[hash];
    //                             if (isFirst)
    //                             {
    //                                 group.Hash = hash;
    //                                 group.Size = length;
    //                                 group.DateModified = hypotheticalDuplicates.GetValueAtIndex(i).Item2;
    //
    //                                 (group.Id, group.ImageHash) = await
    //                                     _dbHelpers.GetImageInfosAsync(group.Hash, hashingToken);
    //
    //                                 if (group.Id == 0)
    //                                 {
    //                                     try
    //                                     {
    //                                         group.ImageHash =
    //                                             _imageHashGenerator.GenerateImageHash(hypotheticalDuplicate);
    //
    //                                         group.Id = await _dbHelpers.CacheHashAsync(group, hashingToken);
    //
    //                                         await progress.Writer.WriteAsync(
    //                                             NotificationType.HashGenerationProgress,
    //                                             cancellationToken: hashingToken);
    //                                     }
    //                                     // An OpenCVException is currently thrown if the file path contains non latin characters
    //                                     catch (CvException ex)
    //                                     {
    //                                         foreach (var duplicate in group.Duplicates)
    //                                         {
    //                                             await _notificationContext.Clients.All.SendAsync("notify",
    //                                                 new Notification(NotificationType.Exception,
    //                                                     $"File {duplicate} : {ex.Message}"), //is not an image
    //                                                 hashingToken);
    //                                         }
    //
    //                                         copiesGroups.TryRemove(group.Hash, out _);
    //                                     }
    //                                     // A NullReferenceException is thrown if SkiaSharp cannot decode an image. There is a high chance it is corrupted
    //                                     catch (NullReferenceException)
    //                                     {
    //                                         foreach (var duplicate in group.Duplicates)
    //                                         {
    //                                             await _notificationContext.Clients.All.SendAsync("notify",
    //                                                 new Notification(NotificationType.Exception,
    //                                                     $"File {duplicate} is corrupted"),
    //                                                 hashingToken);
    //                                         }
    //
    //                                         copiesGroups.TryRemove(group.Hash, out _);
    //                                     }
    //                                 }
    //                                 else
    //                                     await progress.Writer.WriteAsync(NotificationType.HashGenerationProgress,
    //                                         cancellationToken: hashingToken);
    //                             }
    //
    //                             group.Duplicates.Enqueue(hypotheticalDuplicate);
    //                             break;
    //                         }
    //                     }
    //                 });
    //
    //             await _notificationContext.Clients.All.SendAsync("notify",
    //                 new Notification(NotificationType.HashGenerationProgress,
    //                     copiesGroups.Count.ToString()), cancellationToken: cancellationToken);
    //             progress.Writer.Complete();
    //             return copiesGroups.ToDictionary(group => group.Value.Id, group => group.Value);
    //         }, cancellationToken: cancellationToken, creationOptions: TaskCreationOptions.LongRunning,
    //         scheduler: TaskScheduler.Default);
    // }
    //
    // private Task<Task> SendProgress(Channel<NotificationType, NotificationType> cachingChannel,
    //     CancellationToken cancellationToken)
    // {
    //     return Task.Factory.StartNew(async () =>
    //         {
    //             try
    //             {
    //                 var progress = 0;
    //
    //                 await foreach (var progessType in cachingChannel.Reader.ReadAllAsync(
    //                                    cancellationToken: cancellationToken))
    //                 {
    //                     progress++;
    //
    //                     await _notificationContext.Clients.All.SendAsync("notify",
    //                         new Notification(progessType,
    //                             progress.ToString()), cancellationToken: cancellationToken);
    //
    //                     if (decimal.Remainder(progress, 1000) == 0)
    //                         GC.Collect(2);
    //                 }
    //             }
    //             catch (Exception e)
    //             {
    //                 Console.WriteLine(e);
    //                 throw;
    //             }
    //         }, cancellationToken: cancellationToken, creationOptions: TaskCreationOptions.LongRunning,
    //         scheduler: TaskScheduler.Default);
    // }
    //
    // private Task<Task> LinkSimilarImagesGroupsToOneAnother(Dictionary<long, ImagesGroup> imagesGroups,
    //     double degreeOfSimilarity,
    //     Channel<ImagesGroup, ImagesGroup> groupingChannel, Channel<NotificationType, NotificationType> progress,
    //     CancellationToken cancellationToken)
    // {
    //     return Task.Factory.StartNew(async () =>
    //     {
    //         try
    //         {
    //             await Parallel.ForEachAsync(imagesGroups.Keys,
    //                 new ParallelOptions
    //                     { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken },
    //                 async (key, similarityToken) =>
    //                 {
    //                     var currentGroup = imagesGroups[key];
    //
    //                     currentGroup.SimilarImagesGroups = await _dbHelpers.GetSimilarImagesGroupsAlreadyDoneInRange(
    //                         currentGroup.Id,
    //                         degreeOfSimilarity,
    //                         similarityToken);
    //
    //                     var newSimilarities = await _dbHelpers.GetSimilarImagesGroups(
    //                         currentGroup.Id,
    //                         currentGroup.ImageHash!,
    //                         degreeOfSimilarity,
    //                         currentGroup.SimilarImagesGroups,
    //                         similarityToken);
    //
    //                     foreach (var similarity in newSimilarities)
    //                     {
    //                         await _dbHelpers.AddSimilarity(similarity, cancellationToken: similarityToken);
    //                         currentGroup.SimilarImagesGroups.Add(similarity.DuplicateId);
    //                     }
    //
    //                     await progress.Writer.WriteAsync(NotificationType.SimilaritySearchProgress,
    //                         cancellationToken: similarityToken);
    //
    //                     // The current group is sent for grouping if it has at least another similar image or there are  m
    //                     // at least 2 copies of the same image
    //                     if (currentGroup.SimilarImagesGroups.Count > 1 || currentGroup.Duplicates.Count > 1)
    //                         await groupingChannel.Writer.WriteAsync(imagesGroups[key], cancellationToken: similarityToken);
    //                     else
    //                         imagesGroups.Remove(currentGroup.Id);
    //                 });
    //
    //             progress.Writer.Complete();
    //             groupingChannel.Writer.Complete();
    //         }
    //         catch (Exception e)
    //         {
    //             Console.WriteLine(e);
    //             throw;
    //         }
    //     }, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    // }
    //
    // private Task<Task> ProcessGroupsForFinalList(Channel<ImagesGroup, ImagesGroup> groupingChannel,
    //     Dictionary<long, ImagesGroup> imagesGroups,
    //     ConcurrentQueue<File> finalImages, CancellationToken cancellationToken)
    // {
    //     return Task.Factory.StartNew(async () =>
    //     {
    //         var groupingProgress = 0;
    //
    //         var notificationCeiling = decimal.Divide(imagesGroups.Count, 400);
    //
    //         var groupsDone = new HashSet<long>();
    //
    //         try
    //         {
    //             await foreach (var currentImagesGroup in groupingChannel.Reader.ReadAllAsync(
    //                                cancellationToken: cancellationToken))
    //             {
    //                 if (groupsDone.Contains(currentImagesGroup.Id))
    //                 {
    //                     currentImagesGroup.SimilarImagesGroups.ExceptWith(groupsDone);
    //
    //                     if (currentImagesGroup.SimilarImagesGroups.Count == 0)
    //                         imagesGroups.Remove(currentImagesGroup.Id);
    //                 }
    //                 else
    //                 {
    //                     switch (currentImagesGroup.SimilarImagesGroups.Count)
    //                     {
    //                         case > 1:
    //                         case 1 when currentImagesGroup.Duplicates.Count > 1:
    //                             LinkImagesToParentGroup(currentImagesGroup, groupsDone, imagesGroups, finalImages,
    //                                 cancellationToken);
    //
    //                             foreach (var similarImageGroup in currentImagesGroup.SimilarImagesGroups)
    //                             {
    //                                 groupsDone.Add(similarImageGroup);
    //                             }
    //
    //                             imagesGroups.Remove(currentImagesGroup.Id);
    //                             await _notificationContext.Clients.All.SendAsync("notify",
    //                                 new Notification(NotificationType.TotalProgress,
    //                                     (++groupingProgress).ToString()), cancellationToken: cancellationToken);
    //                             break;
    //                         default:
    //                             imagesGroups.Remove(currentImagesGroup.Id);
    //                             break;
    //                     }
    //                 }
    //
    //                 if (decimal.Remainder(groupingProgress, notificationCeiling) == 0)
    //                     GC.Collect();
    //             }
    //         }
    //         catch (Exception e)
    //         {
    //             Console.WriteLine(e);
    //             throw;
    //         }
    //     }, cancellationToken);
    // }
    //
    // private void LinkImagesToParentGroup(ImagesGroup parentGroup,
    //     HashSet<long> groupsDone,
    //     Dictionary<long, ImagesGroup> imagesGroups,
    //     ConcurrentQueue<File> finalImages, CancellationToken token)
    // {
    //     Parallel.ForEach(parentGroup.SimilarImagesGroups,
    //         new ParallelOptions
    //             { CancellationToken = token, MaxDegreeOfParallelism = Environment.ProcessorCount },
    //         similarImagesGroup =>
    //         {
    //             if (similarImagesGroup == parentGroup.Id)
    //             {
    //                 while (!parentGroup.Duplicates.IsEmpty)
    //                 {
    //                     if (parentGroup.Duplicates.TryDequeue(out var image))
    //                         finalImages.Enqueue(new File
    //                         {
    //                             Path = image,
    //                             Size = parentGroup.Size,
    //                             DateModified = parentGroup.DateModified,
    //                             Hash = parentGroup.Hash
    //                         });
    //                 }
    //             }
    //             else
    //             {
    //                 if (!imagesGroups.TryGetValue(similarImagesGroup, out var result))
    //                     return;
    //                 foreach (var image in result.Duplicates)
    //                 {
    //                     finalImages.Enqueue(new File
    //                     {
    //                         Path = image,
    //                         Size = result.Size,
    //                         DateModified = result.DateModified,
    //                         Hash = parentGroup.Hash
    //                     });
    //                 }
    //
    //                 // if (groupsDone.Contains(result.Id))
    //                 // {
    //                 //     result.SimilarImagesGroups.ExceptWith(parentGroup.SimilarImagesGroups);
    //                 //     if (result.SimilarImagesGroups.Count == 0)
    //                 //         imagesGroups.Remove(similarImagesGroup);
    //                 // }
    //             }
    //         });
    // }
}