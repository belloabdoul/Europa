using API.Common.Interfaces;
using API.Features.FindDuplicatesByHash.Interfaces;
using API.Features.FindSimilarAudios.Interfaces;
using SoundFingerprinting;
using SoundFingerprinting.Audio;
using SoundFingerprinting.Emy;
using SoundFingerprinting.InMemory;
using System.Collections.Concurrent;
using File = API.Common.Entities.File;

namespace API.Features.FindSimilarAudios.Implementations
{
    public class SimilarAudiosFinder : ISimilarAudiosFinder
    {
        private readonly IFileTypeIdentifier _fileTypeIdentifier;
        private readonly IModelService _modelService;
        private readonly IAudioService _mediaService;
        private readonly IAudioHashGenerator _audioHashGenerator;
        private readonly IHashGenerator _hashGenerator;
        private readonly object readLock;

        public SimilarAudiosFinder(IFileTypeIdentifier fileTypeIdentifier/*, IModelService modelService, IMediaService mediaService*/, IAudioHashGenerator audioHashGenerator, IHashGenerator hashGenerator)
        {
            _fileTypeIdentifier = fileTypeIdentifier;
            _modelService = new InMemoryModelService();
            _mediaService = new FFmpegAudioService();
            _audioHashGenerator = audioHashGenerator;
            _hashGenerator = hashGenerator;
            readLock = new();
        }

        public async Task<IEnumerable<IGrouping<string, File>>> FindSimilarAudiosAsync(List<string> hypotheticalDuplicates, CancellationToken token)
        {
            Console.InputEncoding = System.Text.Encoding.UTF8;
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            var duplicatedAudios = new ConcurrentBag<File>();

            var tracks = _modelService.GetTrackIds();
            foreach (var track in tracks)
            {
                _modelService.DeleteTrack(track);
            }

            token.ThrowIfCancellationRequested();

            await Task.Factory.StartNew(() =>
            {
                hypotheticalDuplicates.AsParallel()
                .WithDegreeOfParallelism((int)(Environment.ProcessorCount * 0.9))
                .WithCancellation(token)
                .WithExecutionMode(ParallelExecutionMode.ForceParallelism)
                .ForAll(file =>
                {
                    var match = string.Empty;
                    var type = _fileTypeIdentifier.GetFileType(file);
                    if (type.Equals("audio"))
                    {
                        //await semaphore.WaitAsync(token);
                        lock(readLock)
                        {
                            try
                            {
                                match = _audioHashGenerator.GetAudioMatches(file, _modelService, _mediaService);
                                if (string.IsNullOrEmpty(match))
                                {
                                    _audioHashGenerator.GenerateAudioHashes(file, _modelService, _mediaService);
                                    duplicatedAudios.Add(new File(new FileInfo(file), _hashGenerator.GenerateHash(file)));
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine(ex.Message);
                            }
                        }
                        //finally 
                        //{
                        //    semaphore.Release();
                        //}
                        if (!string.IsNullOrEmpty(match))
                        {
                            duplicatedAudios.Add(new File(new FileInfo(file), duplicatedAudios.First(audio => audio.Path.Equals(match)).Hash));
                        }
                    }
                });
            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            token.ThrowIfCancellationRequested();

            token.ThrowIfCancellationRequested();
            return [.. duplicatedAudios.OrderByDescending(file => file.DateModified).GroupBy(file => file.Hash).Where(i => i.Count() != 1)];
        }
    }
}
