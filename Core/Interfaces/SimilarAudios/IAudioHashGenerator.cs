using System.Collections.Concurrent;
using Core.Entities.Audios;

namespace Core.Interfaces.SimilarAudios;

public interface IAudioHashGenerator
{
    ValueTask<IList<Fingerprint>> GenerateAudioHashesAsync(string path, byte[] fileId,
        bool random = false, CancellationToken cancellationToken = default);
}