using Core.Entities.Audios;
using Core.Entities.Commons;
using NSwag.Collections;
using Swordfish.NET.Collections;
using Fingerprint = Core.Entities.Audios.Fingerprint;

namespace Api.Client.Repositories;

public interface ISimilarAudiosRepository
{
    ValueTask<ConcurrentObservableDictionary<byte[], Similarity>> GetExistingMatchesForFileAsync(
        string collectionName, byte[] fileId, CancellationToken cancellationToken = default);

    ValueTask<IEnumerable<KeyValuePair<byte[], decimal>>> GetMatchingFingerprintsAsync(
        string collectionName, IList<Fingerprint> fingerprints, int thresholdVotes, double gapAllowed,
        decimal degreeOfSimilarity, byte[] fileId, ICollection<byte[]> existingMatches,
        Dictionary<byte[], int> filesWithFingerprintsCount, CancellationToken cancellationToken = default);

    ValueTask<bool> LinkToSimilarFilesAsync(string collectionName, byte[] id,
        ICollection<Similarity> newSimilarities, CancellationToken cancellationToken = default);
}