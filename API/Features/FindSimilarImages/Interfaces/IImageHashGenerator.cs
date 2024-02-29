using SoundFingerprinting.Audio;
using SoundFingerprinting;
using System.Collections.Concurrent;

namespace API.Features.FindSimilarImages.Interfaces
{
    public interface IImageHashGenerator
    {
        string GenerateImageHash(string path, string type);
        List<string> GenerateAnimatedImageHashes(string path, string type);
    }
}
