using SoundFingerprinting;
using SoundFingerprinting.Audio;

namespace Core.Interfaces;

public interface IAudioHashGenerator
{
    string GetAudioMatches(string path, IModelService modelService, IAudioService mediaService);
    void GenerateAudioHashes(string path, IModelService modelService, IAudioService mediaService);
}