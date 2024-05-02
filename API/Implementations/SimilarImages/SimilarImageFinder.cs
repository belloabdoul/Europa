using Database.Interfaces;
using API.Implementations.Common;
using Microsoft.AspNetCore.SignalR;
using Core.Interfaces.SimilarImages;
using Core.Interfaces.Common;
using Core.Entities;
using File = Core.Entities.File;
using Core.Interfaces.DuplicatesByHash;
using System.Collections.Concurrent;
using System.Threading.Channels;
using OpenCvSharp;

namespace API.Implementations.SimilarImages
{
    public class SimilarImageFinder : ISimilarImagesFinder
    {
        private readonly IHubContext<NotificationHub> _notificationContext;
        private readonly IFileTypeIdentifier _fileTypeIdentifier;

        private readonly IHashGenerator _hashGenerator;

        private readonly IImageHashGenerator _imageHashGenerator;
        private readonly IDbHelpers _dbHelpers;
        private readonly IFileReader _fileReader;

        public SimilarImageFinder(IHubContext<NotificationHub> notificationContext, IFileReader fileReader,
            IFileTypeIdentifier fileTypeIdentifier, IHashGenerator hashGenerator,
            IImageHashGenerator imageHashGenerator, IDbHelpers dbHelpers)
        {
            _notificationContext = notificationContext;
            _fileReader = fileReader;
            _fileTypeIdentifier = fileTypeIdentifier;
            _hashGenerator = hashGenerator;
            _imageHashGenerator = imageHashGenerator;
            _dbHelpers = dbHelpers;
        }

        public async Task<IEnumerable<IGrouping<string, File>>> FindSimilarImagesAsync(
            List<string> hypotheticalDuplicates, double degreeOfSimilarity, CancellationToken token)
        {
            var copiesGroups =
                new ConcurrentDictionary<string, FileGroup>();

            var cachingPipeline =
                GenerateAndCacheImageHashForNonCorruptedFiles(hypotheticalDuplicates, copiesGroups, token);

            await cachingPipeline;

            if (!cachingPipeline.IsCompletedSuccessfully)
                return [];

            var groupingPipeline = Channel.CreateUnbounded<string>();

            var similarityTask =
                LinkSimilarImagesGroupsToOneAnother(copiesGroups, groupingPipeline, degreeOfSimilarity, token);

            var finalImages = new ConcurrentQueue<File>();

            var groupingTask =
                ProcessGroupsForFinalList(groupingPipeline, copiesGroups, degreeOfSimilarity, finalImages, token);

            await Task.WhenAll(await similarityTask, await groupingTask);

            //.OrderByDescending(file => file.DateModified)
            var groups = finalImages.GroupBy(file => file.Id)
                .Where(i => i.Count() != 1).ToList();
            return groups;
        }

        private async Task GenerateAndCacheImageHashForNonCorruptedFiles(
            List<string> hypotheticalDuplicates,
            ConcurrentDictionary<string, FileGroup> copiesGroups, CancellationToken token)
        {
            var cachingProgress = 0;
            var start = 0;
            var filesWithoutHash = new ConcurrentQueue<File>();
            var filesToCache = new ConcurrentQueue<File>();
            while (start != hypotheticalDuplicates.Count)
            {
                var count = 2500;
                if (start + count > hypotheticalDuplicates.Count)
                    count = hypotheticalDuplicates.Count - start;

                // Generate integrity hash and group perfect copies together
                await Parallel.ForEachAsync(hypotheticalDuplicates.GetRange(start, count),
                    new ParallelOptions
                        { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = token },
                    async (path, cachingToken) =>
                    {
                        var file = new File(new FileInfo(path))
                        {
                            FileType = _fileTypeIdentifier.GetFileType(path)
                        };
                        switch (file.FileType)
                        {
                            case FileType.Corrupt:
                                await _notificationContext.Clients.All.SendAsync("Notify",
                                    new Notification(NotificationType.Exception,
                                        $"File {file.Path} is corrupted"), cancellationToken: cachingToken);
                                break;
                            case FileType.Image or FileType.GifImage:
                            {
                                await using var stream = _fileReader.GetFileStream(path);
                                file.Id = _hashGenerator.GenerateHash(stream, file.Size);

                                var isFirst = copiesGroups.TryAdd(file.Id, new FileGroup(file));
                                if (isFirst)
                                    filesWithoutHash.Enqueue(file);
                                else
                                {
                                    copiesGroups[file.Id].Files.Enqueue(file);
                                }

                                break;
                            }
                        }
                    });


                // Get the cached image hash for each group of perfect copies without image hash if it exists
                await _dbHelpers.GetCachedHashAsync(filesWithoutHash, token);

                await Parallel.ForAsync(0, filesWithoutHash.Count,
                    new ParallelOptions
                        { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = token },
                    async (_, cachingToken) =>
                    {
                        filesWithoutHash.TryDequeue(out var file);

                        if (file!.ImageHash == null || file.ImageHash.Memory.Length == 0)
                        {
                            try
                            {
                                await using var stream = _fileReader.GetFileStream(file.Path);

                                file.ImageHash = _imageHashGenerator.GenerateImageHash(stream);

                                Interlocked.Increment(ref cachingProgress);
                                var current = Interlocked.CompareExchange(ref cachingProgress, 0, 0);

                                await _notificationContext.Clients.All.SendAsync("Notify",
                                    new Notification(NotificationType.HashGenerationProgress,
                                        current.ToString()), cancellationToken: cachingToken);

                                filesToCache.Enqueue(file);
                            }
                            // An OpenCVException is currently thrown if the file path contains non latin characters
                            catch (OpenCVException)
                            {
                                await _notificationContext.Clients.All.SendAsync("Notify",
                                    new Notification(NotificationType.Exception,
                                        $"File {file.Path} is not an image"),
                                    cancellationToken: cachingToken);
                            }
                            // A NullReferenceException is thrown if SkiaSharp cannot decode an image. There is a high chance it is corrupted
                            catch (NullReferenceException)
                            {
                                await _notificationContext.Clients.All.SendAsync("Notify",
                                    new Notification(NotificationType.Exception,
                                        $"File {file.Path} is corrupted"),
                                    cancellationToken: cachingToken);
                            }
                        }
                        else
                        {
                            Interlocked.Increment(ref cachingProgress);
                            var current = Interlocked.CompareExchange(ref cachingProgress, 0, 0);

                            await _notificationContext.Clients.All.SendAsync("Notify",
                                new Notification(NotificationType.HashGenerationProgress,
                                    current.ToString()), cancellationToken: cachingToken);
                        }
                    });
                await _dbHelpers.CacheHashAsync(filesToCache, token);
                filesWithoutHash.Clear();
                filesToCache.Clear();
                start += count;
                GC.Collect();
            }

            await _notificationContext.Clients.All.SendAsync("Notify",
                new Notification(NotificationType.HashGenerationProgress,
                    cachingProgress.ToString()), cancellationToken: token);
        }

        private Task<Task> LinkSimilarImagesGroupsToOneAnother(
            ConcurrentDictionary<string, FileGroup> copiesGroups, Channel<string, string> groupingPipeline,
            double degreeOfSimilarity,
            CancellationToken token)
        {
            return Task.Factory.StartNew(async () =>
            {
                var similarityProgress = 0;
                var start = 0;
                var keys = copiesGroups.Keys.ToList();
                while (start != keys.Count)
                {
                    var count = 2500;
                    if (start + count > keys.Count)
                        count = keys.Count - start;
                    await Parallel.ForEachAsync(keys.GetRange(start, count),
                        new ParallelOptions
                            { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = token },
                        async (key, similarityToken) =>
                        {
                            var similarImagesGroupsAssociatedToCurrent =
                                await _dbHelpers.GetSimilarImagesGroupsAssociatedToAsync(key,
                                    cancellationToken: similarityToken);

                            var newSimilarImagesGroups = await _dbHelpers.GetSimilarImagesGroups(
                                copiesGroups[key].Files.First().ImageHash!, degreeOfSimilarity,
                                similarImagesGroupsAssociatedToCurrent,
                                cancellationToken: similarityToken);

                            Parallel.For(0, newSimilarImagesGroups.Count,
                                new ParallelOptions
                                {
                                    MaxDegreeOfParallelism = Environment.ProcessorCount,
                                    CancellationToken = similarityToken
                                }, i => { newSimilarImagesGroups[i].HypotheticalOriginalId = key; });

                            await _dbHelpers.SetSimilaritiesAsync(newSimilarImagesGroups,
                                cancellationToken: similarityToken);

                            newSimilarImagesGroups.Clear();
                            similarImagesGroupsAssociatedToCurrent.Clear();

                            Interlocked.Increment(ref similarityProgress);

                            var current = Interlocked.CompareExchange(ref similarityProgress, 0, 0);

                            await _notificationContext.Clients.All.SendAsync("Notify",
                                new Notification(NotificationType.SimilaritySearchProgress, current.ToString()),
                                cancellationToken: similarityToken);

                            await groupingPipeline.Writer.WriteAsync(key, cancellationToken: similarityToken);
                        });
                    start += count;
                    GC.Collect();
                }

                groupingPipeline.Writer.Complete();
            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private void LinkImagesToParentGroup(string parentGroup,
            IEnumerable<string> similarImagesGroups,
            ConcurrentDictionary<string, FileGroup> copiesGroups,
            ConcurrentQueue<File> finalImages, CancellationToken token)
        {
            Parallel.ForEach(similarImagesGroups,
                new ParallelOptions
                    { CancellationToken = token, MaxDegreeOfParallelism = Environment.ProcessorCount },
                group =>
                {
                    if (group.Equals(parentGroup))
                    {
                        Parallel.For(0, copiesGroups[group].Files.Count,
                            new ParallelOptions
                                { CancellationToken = token, MaxDegreeOfParallelism = Environment.ProcessorCount },
                            _ =>
                            {
                                if (copiesGroups[group].Files.TryDequeue(out var image))
                                {
                                    finalImages.Enqueue(image);
                                }
                            });
                    }
                    else
                    {
                        if (!copiesGroups.TryGetValue(group, out var result))
                            return;
                        Parallel.ForEach(result.Files,
                            new ParallelOptions
                                { CancellationToken = token, MaxDegreeOfParallelism = Environment.ProcessorCount },
                            image => { finalImages.Enqueue(new File(image) { Id = parentGroup }); });
                    }
                });
        }

        private Task<Task> ProcessGroupsForFinalList(Channel<string, string> similarImagesGroups,
            ConcurrentDictionary<string, FileGroup> copiesGroups, double degreeOfSimilarity,
            ConcurrentQueue<File> finalImages, CancellationToken token)
        {
            return Task.Factory.StartNew(async () =>
            {
                var groupingProgress = 0;

                var notificationCeiling = decimal.Divide(copiesGroups.Count, 400);

                var groupsDone = new ConcurrentDictionary<string, byte>();

                await foreach (var key in similarImagesGroups.Reader.ReadAllAsync(token))
                {
                    if (!groupsDone.ContainsKey(key))
                    {
                        var similarImagesGroupsInRangeAsync =
                            await _dbHelpers.GetSimilarImagesGroupsInRangeAsync(key, degreeOfSimilarity, token);
                        LinkImagesToParentGroup(key, similarImagesGroupsInRangeAsync, copiesGroups, finalImages,
                            token);

                        Parallel.ForEach(similarImagesGroupsInRangeAsync,
                            new ParallelOptions
                                { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = token },
                            group => { groupsDone[group] = 1; });
                    }

                    await _notificationContext.Clients.All.SendAsync("Notify",
                        new Notification(NotificationType.TotalProgress,
                            (++groupingProgress).ToString()), cancellationToken: token);

                    if (decimal.Remainder(groupingProgress, notificationCeiling) <= 1)
                        GC.Collect();
                }
            }, token);
        }
    }
}