using SoundFingerprinting;
using SoundFingerprinting.Emy;
using SoundFingerprinting.Media;

namespace API.Features.FindSimilarAudios.Interfaces
{
    public interface IAudioHashGenerator
    {
        string GenerateHash(string path);
        string GetAudioMatches(string path, IModelService modelService, IMediaService mediaService);
        void GenerateAudioHashes(string path, IModelService modelService, IMediaService mediaService);
    }
}
