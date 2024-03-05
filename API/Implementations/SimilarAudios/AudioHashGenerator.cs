using API.Interfaces.SimilarAudios;
using SoundFingerprinting;
using SoundFingerprinting.Audio;
using SoundFingerprinting.Builder;
using SoundFingerprinting.Data;
using SoundFingerprinting.Strides;

namespace API.Implementations.SimilarAudios
{
    public class AudioHashGenerator : IAudioHashGenerator
    {
        public void GenerateAudioHashes(string path, IModelService modelService, IAudioService mediaService)
        {
            var track = new TrackInfo(path, Path.GetFileNameWithoutExtension(path), string.Empty);

            var hashedFingerprints = FingerprintCommandBuilder
                .Instance
                .BuildFingerprintCommand()
                .From(path)
                .WithFingerprintConfig(config =>
                {
                    config.Audio.Stride = new IncrementalStaticStride(4096);
                    return config;
                })
                .UsingServices(mediaService)
                .Hash().Result;

            modelService.Insert(track, hashedFingerprints);
        }

        public string GetAudioMatches(string path, IModelService modelService, IAudioService mediaService)
        {
            var result = QueryCommandBuilder
                .Instance
                .BuildQueryCommand()
                .From(path)
                .WithQueryConfig(config =>
                {
                    config.Audio.ThresholdVotes = 25;
                    config.Audio.Stride = new IncrementalRandomStride(2048, 4096);
                    return config;
                })
                .UsingServices(modelService, mediaService)
                .Query().Result;

            if (result.BestMatch != null)
                return result.BestMatch.TrackId;
            return string.Empty;
        }
    }
}
