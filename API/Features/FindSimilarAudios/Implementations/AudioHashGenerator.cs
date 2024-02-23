using API.Features.FindSimilarAudios.Interfaces;
using Blake3;
using SoundFingerprinting;
using SoundFingerprinting.Audio;
using SoundFingerprinting.Builder;
using SoundFingerprinting.Data;
using SoundFingerprinting.Emy;
using SoundFingerprinting.Media;
using SoundFingerprinting.Strides;
using System.Buffers;

namespace API.Features.FindSimilarAudios.Implementations
{
    public class AudioHashGenerator : IAudioHashGenerator
    {
        public string GenerateHash(string path)
        {
            using var stream = File.OpenRead(path);

            using var hasher = Hasher.New();
            var buffer = ArrayPool<byte>.Shared.Rent(4096);

            int bytesRead;
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                hasher.UpdateWithJoin(buffer.AsSpan(0, bytesRead));
            }

            ArrayPool<byte>.Shared.Return(buffer);

            return hasher.Finalize().ToString();
        }

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
