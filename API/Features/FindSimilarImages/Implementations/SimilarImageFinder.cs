using API.Features.FindSimilarImages.Interfaces;
using System.Collections.Concurrent;
using File = API.Common.Entities.File;
using API.Common.Interfaces;
using Database.Interfaces;
using OpenCvSharp;
using API.Features.FindDuplicatesByHash.Interfaces;

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

            TaskScheduler scheduler = TaskScheduler.Default;

            // We create a task for filtering only images, creating their blake3 hash,
            // then grouping them by hash. With this copies of the same file will be
            // processed in the next phase only once.
            using var distinctImagesQueue = new BlockingCollection<(string Path, string Type, string FileHash)>();

            Task blake3Producer = Task.Factory.StartNew(() =>
            {
                var perfectDuplicatesGroups = hypotheticalDuplicates
                .AsParallel()
                .WithDegreeOfParallelism((int)(Environment.ProcessorCount * 0.9))
                .WithExecutionMode(ParallelExecutionMode.ForceParallelism)
                .WithCancellation(token)
                .WithMergeOptions(ParallelMergeOptions.FullyBuffered)
                .Select(file => (Path: file, Type: _fileTypeIdentifier.GetFileType(file)))
                .GroupBy(file => file.Type)
                .Where(group => group.Key.Contains(_commonType))
                .SelectMany(group => group.ToList())
                .Select(file => (file.Path, file.Type, Hash: _hashGenerator.GenerateHash(file.Path)))
                .ToLookup(file => file.Hash, file => (file.Path, file.Type, file.Hash));


                perfectDuplicatesGroups.AsParallel()
                .WithDegreeOfParallelism((int)(Environment.ProcessorCount * 0.9))
                .WithExecutionMode(ParallelExecutionMode.ForceParallelism)
                .WithCancellation(token)
                .ForAll(group =>
                {
                    var images = group.ToList();
                    foreach (var image in images)
                    {
                        duplicatedImages.Add(new File(new FileInfo(image.Path), group.Key));
                    }
                    distinctImagesQueue.Add((images[Random.Shared.Next(0, images.Count)].Path, group.First().Type, group.Key));
                });
                distinctImagesQueue.CompleteAdding();
            }, token, TaskCreationOptions.LongRunning, scheduler);

            // We create a task for generating an image hash for each group
            // of perfect duplicates. 
            using var imageHashQueue = new BlockingCollection<(string Hash, string Path)>();

            Task producer = Task.Factory.StartNew(() =>
            {
                distinctImagesQueue.GetConsumingEnumerable()
                .AsParallel()
                //.AsOrdered()
                .WithDegreeOfParallelism((int)(Environment.ProcessorCount * 0.9))
                .WithExecutionMode(ParallelExecutionMode.ForceParallelism)
                .WithMergeOptions(ParallelMergeOptions.NotBuffered)
                .WithCancellation(token)
                .ForAll(image =>
                {
                    try
                    {
                        var hash = _imageHashGenerator.GenerateImageHash(image.Path, image.Type);

                        // Here we change the hash from the cryptographic one to the perceptual one
                        foreach (var duplicate in duplicatedImages.Where(duplicate => duplicate.Hash.Equals(image.FileHash)))
                        {
                            duplicate.Hash = hash;
                        }

                        // We only continue with the hash because with the hash because the type
                        // and the path where only needed to generate the perceptual hash.
                        // The same way the cryptographic hash is already replaced with the
                        // perceptual hash in the previous instructions
                        imageHashQueue.Add((hash, image.Path));
                    }
                    // An OpenCVException is currently thrown if the file path contains non latin characters.
                    catch (OpenCVException)
                    {
                        exceptions.Enqueue($"File {image.Path} is not an image");
                    }
                    // A NullReferenceException is thrown if SkiaSharp cannot decode an image. There is a high chance it is corrupted.
                    catch (NullReferenceException)
                    {
                        exceptions.Enqueue($"File {image.Path} is corrupted");
                    }
                });
                imageHashQueue.CompleteAdding();
            }, token, TaskCreationOptions.LongRunning, scheduler);

            // Here we create the consumer which will process each hash. It checks if the current image/image group has a similar image
            // in the redis database using its vector similarity unctions. If yes, its duplicates are update with the hash associated to
            // our image found. It will only be added if no similar images are found in the databse. 

            var imagesIndexToHash = new ConcurrentDictionary<int, string>();

            Task duplicatesProcessor = Task.Factory.StartNew(() =>
            {
                foreach (var imageInfo in imageHashQueue.GetConsumingEnumerable().Select((hash, index) => (hash.Hash, hash.Path, Position: index)))
                {
                    var similarHashIndex = _dbHelpers.GetSimilarHashIndex(imageInfo.Hash, imageInfo.Position, 1).FirstOrDefault();
                    if (similarHashIndex == 0)
                    {
                        _dbHelpers.InsertPartialImageHash(imageInfo.Hash, imageInfo.Position, imageInfo.Path);
                    }

                    if (similarHashIndex == 0)
                    {
                        imagesIndexToHash[imageInfo.Position] = imageInfo.Hash;
                    }
                    else
                    {
                        var duplicates = duplicatedImages.Where(image => image.Hash.Equals(imageInfo.Hash)).ToList();
                        foreach (var duplicate in duplicates)
                        {
                            duplicate.Hash = imagesIndexToHash[similarHashIndex];
                        }
                    }
                }
            }, token, TaskCreationOptions.LongRunning, scheduler);

            await Task.WhenAll(blake3Producer, producer, duplicatesProcessor);

            token.ThrowIfCancellationRequested();

            return ([.. duplicatedImages.OrderByDescending(file => file.DateModified).GroupBy(file => file.Hash).Where(i => i.Count() != 1)], exceptions);
        }
    }
}