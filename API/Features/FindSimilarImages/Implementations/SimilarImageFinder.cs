using API.Features.FindSimilarImages.Interfaces;
using System.Collections.Concurrent;
using File = API.Common.Entities.File;
using API.Common.Interfaces;
using Database.Interfaces;
using OpenCvSharp;
using API.Features.FindDuplicatesByHash.Interfaces;
using System.Threading.Channels;

namespace API.Features.FindSimilarImages.Implementations
{
    public class SimilarImageFinder : ISimilarImagesFinder
    {
        private readonly IFileTypeIdentifier _fileTypeIdentifier;
        private readonly IImageHashGenerator _imageHashGenerator;
        private readonly IHashGenerator _hashGenerator;
        private readonly IDbHelpers _dbHelpers;
        private readonly string _commonType;

        public SimilarImageFinder(IFileTypeIdentifier fileTypeIdentifier, IImageHashGenerator imageHashGenerator, IHashGenerator hashGenerator, IDbHelpers dbHelpers)
        {
            _fileTypeIdentifier = fileTypeIdentifier;
            _imageHashGenerator = imageHashGenerator;
            _hashGenerator = hashGenerator;
            _dbHelpers = dbHelpers;
            _commonType = "image";
        }

        public async Task<(IEnumerable<IGrouping<string, File>>, ConcurrentQueue<string>)> FindSimilarImagesAsync(List<string> hypotheticalDuplicates, CancellationToken token)
        {
            // The final list to return to the controller
            var duplicatedImages = new ConcurrentBag<File>();

            // The collections of errors which happened during the processing
            var exceptions = new ConcurrentQueue<string>();

            // We create a task for generating an image hash for each group
            // of perfect duplicates. 
            //using var imageHashQueue = new BlockingCollection<(string Path, string Hash)>();
            var imageHashQueue = Channel.CreateUnbounded<(string Path, string Hash)>();

            Task producer = Task.Factory.StartNew(() =>
            {
                hypotheticalDuplicates
                .AsParallel()
                //.AsOrdered()
                .WithDegreeOfParallelism((int)(Environment.ProcessorCount * 0.9))
                .WithExecutionMode(ParallelExecutionMode.ForceParallelism)
                .WithCancellation(token)
                .ForAll(image =>
                {
                    try
                    {
                        var type = _fileTypeIdentifier.GetFileType(image);
                        if (type.Contains(_commonType))
                        {
                            var hash = _imageHashGenerator.GenerateImageHash(image, type);

                            // We only continue with the hash because with the hash because the type
                            // and the path where only needed to generate the perceptual hash.
                            // The same way the cryptographic hash is already replaced with the
                            // perceptual hash in the previous instructions
                            imageHashQueue.Writer.TryWrite((Path: image, Hash: hash));
                        }
                    }
                    // An OpenCVException is currently thrown if the file path contains non latin characters.
                    catch (OpenCVException)
                    {
                        exceptions.Enqueue($"File {image} is not an image");
                    }
                    // A NullReferenceException is thrown if SkiaSharp cannot decode an image. There is a high chance it is corrupted.
                    catch (NullReferenceException)
                    {
                        exceptions.Enqueue($"File {image} is corrupted");
                    }
                });

                imageHashQueue.Writer.Complete();
            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            // Here we create the consumer which will process each hash. It checks if the current image/image group has a similar image
            // in the redis database using its vector similarity unctions. If yes, its duplicates are update with the hash associated to
            // our image found. It will only be added if no similar images are found in the databse. 

            var imagesIndexToHash = new ConcurrentDictionary<int, string>();

            Task duplicatesProcessor = Task.Factory.StartNew(() =>
            {
                foreach (var imageInfo in imageHashQueue.Reader.ReadAllAsync().ToBlockingEnumerable().Select((hash, index) => (hash.Path, Position: index, hash.Hash)))
                {
                    Console.WriteLine($"{imageInfo.Path} {imageInfo.Position}");
                    var similarHashIndex = _dbHelpers.GetSimilarHashIndex(imageInfo.Hash, imageInfo.Position, 1).FirstOrDefault();
                    if (similarHashIndex == 0)
                    {
                        _dbHelpers.InsertPartialImageHash(imageInfo.Hash, imageInfo.Position, imageInfo.Path);
                        imagesIndexToHash[imageInfo.Position] = imageInfo.Hash;
                        duplicatedImages.Add(new File(new FileInfo(imageInfo.Path), imageInfo.Hash));
                    }
                    else
                    {
                        duplicatedImages.Add(new File(new FileInfo(imageInfo.Path), imagesIndexToHash[similarHashIndex]));
                    }
                }
            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            await Task.WhenAll(producer, duplicatesProcessor);

            token.ThrowIfCancellationRequested();

            return ([.. duplicatedImages.OrderByDescending(file => file.DateModified).GroupBy(file => file.Hash).Where(i => i.Count() != 1)], exceptions);
        }
    }
}