using System.Collections.Concurrent;
using System.Text;
using Core.Interfaces;
using Core.Interfaces.Common;
using SoundFingerprinting;
using SoundFingerprinting.Audio;
using SoundFingerprinting.Emy;
using File = Core.Entities.File;

namespace API.Implementations.SimilarAudios;

public class SimilarAudiosFinder : ISimilarFilesFinder
{
    private readonly IAudioHashGenerator _audioHashGenerator;
    private readonly List<IFileTypeIdentifier> _audioIdentifiers;
    private readonly IHashGenerator _hashGenerator;
    private readonly IAudioService _mediaService;
    private readonly IModelService _modelService;
    private readonly object readLock;

    public SimilarAudiosFinder(List<IFileTypeIdentifier> audioIdentifiers,
        IAudioHashGenerator audioHashGenerator, IHashGenerator hashGenerator)
    {
        _audioIdentifiers = audioIdentifiers;
        _audioHashGenerator = audioHashGenerator;
        _hashGenerator = hashGenerator;
        _modelService = EmyModelService.NewInstance("localhost", 3399);
        _mediaService = new FFmpegAudioService();
        readLock = new object();
    }

    public int DegreeOfSimilarity { get; set; }

    public async Task<IEnumerable<IGrouping<string, File>>> FindSimilarFilesAsync(string[] hypotheticalDuplicates,
        CancellationToken token)
    {
        Console.InputEncoding = Encoding.UTF8;
        Console.OutputEncoding = Encoding.UTF8;

        var duplicatedAudios = new ConcurrentBag<File>();

        var tracks = _modelService.GetTrackIds();
        foreach (var track in tracks) _modelService.DeleteTrack(track);

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
                    // var type = _fileTypeIdentifier.GetFileType(file);
                    // if (type.Equals("audio"))
                    // {
                    //     //await semaphore.WaitAsync(token);
                    //     lock (readLock)
                    //     {
                    //         try
                    //         {
                    //             match = _audioHashGenerator.GetAudioMatches(file, _modelService, _mediaService);
                    //             if (string.IsNullOrEmpty(match))
                    //             {
                    //                 _audioHashGenerator.GenerateAudioHashes(file, _modelService, _mediaService);
                    //                 using var fileStream = _fileReader.GetFileStream(file);
                    //                 // duplicatedAudios.Add(new File(new FileInfo(file), _hashGenerator.GenerateHash(fileStream)));
                    //             }
                    //         }
                    //         catch (Exception ex)
                    //         {
                    //             Console.WriteLine(ex.Message);
                    //         }
                    //     }
                    //     //finally 
                    //     //{
                    //     //    semaphore.Release();
                    //     //}
                    //     if (!string.IsNullOrEmpty(match))
                    //     {
                    //         duplicatedAudios.Add(new File(new FileInfo(file), duplicatedAudios.First(audio => audio.Path.Equals(match)).Hash));
                    //     }
                    // }
                });
        }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        token.ThrowIfCancellationRequested();

        token.ThrowIfCancellationRequested();
        return
        [
            .. duplicatedAudios.OrderByDescending(file => file.DateModified).GroupBy(file => file.Hash)
                .Where(i => i.Count() != 1)
        ];
    }
}