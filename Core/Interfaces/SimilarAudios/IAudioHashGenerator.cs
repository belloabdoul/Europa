using Core.Entities.Audios;

namespace Core.Interfaces.SimilarAudios;

public interface IAudioHashGenerator
{
    Profile FingerprintingConfiguration { get; }
    ValueTask<List<Fingerprint>> GenerateAudioHashesAsync(string path, byte[] fileId,
        bool random = false, CancellationToken cancellationToken = default);
}