﻿using SoundFingerprinting;
using SoundFingerprinting.Audio;

namespace API.Features.FindSimilarAudios.Interfaces
{
    public interface IAudioHashGenerator
    {
        string GenerateHash(string path);
        string GetAudioMatches(string path, IModelService modelService, IAudioService mediaService);
        void GenerateAudioHashes(string path, IModelService modelService, IAudioService mediaService);
    }
}
