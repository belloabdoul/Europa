using System.Collections.Concurrent;
using File = API.Common.Entities.File;
using Database.Interfaces;
using OpenCvSharp;
using System.Threading.Channels;
using API.Interfaces.Common;
using API.Interfaces.SimilarImages;
using API.Implementations.Common;
using Microsoft.AspNetCore.SignalR;
using API.Entities;

namespace API.Implementations.SimilarImages
{
    public class SimilarImageFinder : ISimilarImagesFinder
    {
        private readonly IHubContext<NotificationHub> _notificationContext;
        private readonly IFileTypeIdentifier _fileTypeIdentifier;
        private readonly IImageHashGenerator _imageHashGenerator;
        private readonly IDbHelpers _dbHelpers;
        private readonly IFileReader _fileReader;
        private readonly string _commonType;

        public SimilarImageFinder(IHubContext<NotificationHub> notificationContext, IFileReader fileReader, IFileTypeIdentifier fileTypeIdentifier, IImageHashGenerator imageHashGenerator, IDbHelpers dbHelpers)
        {
            _notificationContext = notificationContext;
            _fileReader = fileReader;
            _fileTypeIdentifier = fileTypeIdentifier;
            _imageHashGenerator = imageHashGenerator;
            _dbHelpers = dbHelpers;
            _commonType = "image";
        }

        public async Task<IEnumerable<IGrouping<string, File>>> FindSimilarImagesAsync(List<string> hypotheticalDuplicates, CancellationToken token)
        {
            var duplicatedImages = new ConcurrentBag<File>();

            var imageHashesQueue = Channel.CreateUnbounded<(string Path, string Hash)>();

            var notificationsQueue = Channel.CreateUnbounded<Notification>();

            Task producer = Task.Factory.StartNew(() =>
            {
                hypotheticalDuplicates
                .AsParallel()
                .WithDegreeOfParallelism((int)(Environment.ProcessorCount * 0.9))
                .WithExecutionMode(ParallelExecutionMode.ForceParallelism)
                .WithCancellation(token)
                .ForAll(async image =>
                {
                    try
                    {
                        var type = _fileTypeIdentifier.GetFileType(image);
                        if (type.Contains(_commonType))
                        {
                            using var imageStream = _fileReader.GetFileStream(image);
                            var hash = _imageHashGenerator.GenerateImageHash(imageStream, type);

                            await imageHashesQueue.Writer.WriteAsync((Path: image, Hash: hash));
                        }
                    }
                    // An OpenCVException is currently thrown if the file path contains non latin characters.
                    catch (OpenCVException)
                    {
                        await notificationsQueue.Writer.WriteAsync(new Notification(NotificationType.Exception, $"File {image} is not an image"));
                    }
                    // A NullReferenceException is thrown if SkiaSharp cannot decode an image. There is a high chance it is corrupted.
                    catch (NullReferenceException)
                    {
                        await notificationsQueue.Writer.WriteAsync(new Notification(NotificationType.Exception, $"File {image} is corrupted"));
                    }

                    await notificationsQueue.Writer.WriteAsync(new Notification(NotificationType.HashGenerationProgress, string.Empty));
                });

                imageHashesQueue.Writer.Complete();
            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);


            // This consumer will process each hash. It checks if the current image in the redis database using its vector similarity functions.
            // If no duplicates are found, the hash is inserted into the database. if a duplicate is found, the hash of the current image is replaced with its duplicate found.
            var imagesIndexToHash = new ConcurrentDictionary<int, string>();

            Task duplicatesProcessor = Task.Factory.StartNew(async () =>
            {
                foreach (var imageInfo in imageHashesQueue.Reader.ReadAllAsync(token).ToBlockingEnumerable().Select((hash, index) => (hash.Path, Position: index, hash.Hash)))
                {
                    var similarHashIndex = _dbHelpers.GetSimilarHashIndex(imageInfo.Hash, imageInfo.Position, 1).FirstOrDefault();
                    if (similarHashIndex == 0)
                    {
                        _dbHelpers.InsertImageHash(imageInfo.Hash, imageInfo.Position, imageInfo.Path);
                        imagesIndexToHash[imageInfo.Position] = imageInfo.Hash;
                        duplicatedImages.Add(new File(new FileInfo(imageInfo.Path), imageInfo.Hash));
                    }
                    else
                    {
                        duplicatedImages.Add(new File(new FileInfo(imageInfo.Path), imagesIndexToHash[similarHashIndex]));
                        await notificationsQueue.Writer.WriteAsync(new Notification(NotificationType.File, string.Concat(imagesIndexToHash[similarHashIndex], ", ", imageInfo.Path)));
                    }
                    await notificationsQueue.Writer.WriteAsync(new Notification(NotificationType.TotalProgress, string.Empty));
                }

                notificationsQueue.Writer.Complete();
            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);


            // This consumer is for sending each notification collected to the client using SignalR 
            Task notificationsProcessor = Task.Factory.StartNew(async () =>
            {
                var hashGenerationProgress = 0;
                var totalProgress = 0;
                await foreach (var notification in notificationsQueue.Reader.ReadAllAsync(token))
                {
                    if(notification.Type == NotificationType.HashGenerationProgress)
                    {
                        hashGenerationProgress++;
                        notification.Result = ((double) hashGenerationProgress /  hypotheticalDuplicates.Count * 100).ToString();
                    }
                    else if (notification.Type == NotificationType.TotalProgress)
                    {
                        totalProgress++;
                        notification.Result = ((double)totalProgress / hypotheticalDuplicates.Count * 100).ToString();
                    }
                    await _notificationContext.Clients.All.SendAsync("Notify", notification);
                }
                if(totalProgress != hashGenerationProgress)
                {
                    await _notificationContext.Clients.All.SendAsync("Notify", new Notification(NotificationType.TotalProgress, ((double)hashGenerationProgress / hypotheticalDuplicates.Count * 100).ToString()));
                }
            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            await Task.WhenAll(producer, duplicatesProcessor, notificationsProcessor);

            token.ThrowIfCancellationRequested();

            return [.. duplicatedImages.OrderByDescending(file => file.DateModified).GroupBy(file => file.Hash).Where(i => i.Count() != 1)];
        }
    }
}