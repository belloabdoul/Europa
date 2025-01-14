using Core.Entities.Commons;
using Swordfish.NET.Collections;
using Fingerprint = Core.Entities.Audios.Fingerprint;

namespace Api.Client.Repositories;

public interface ISimilarAudiosRepository
{
    ValueTask<ConcurrentObservableDictionary<byte[], Similarity>> GetExistingMatchesForFileAsync(byte[] fileId,
        CancellationToken cancellationToken = default);

    ValueTask<IEnumerable<KeyValuePair<byte[], decimal>>> GetMatchingFingerprintsAsync(IList<Fingerprint> fingerprints,
        int thresholdVotes, double gapAllowed,
        decimal degreeOfSimilarity, byte[] fileId, ICollection<byte[]> existingMatches,
        Dictionary<byte[], int> filesWithFingerprintsCount, CancellationToken cancellationToken = default);

    ValueTask<bool> LinkToSimilarFilesAsync(ICollection<Similarity> newSimilarities,
        CancellationToken cancellationToken = default);
}