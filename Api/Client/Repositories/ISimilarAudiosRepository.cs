using Core.Entities.Images;
using NSwag.Collections;
using Fingerprint = Core.Entities.Audios.Fingerprint;

namespace Api.Client.Repositories;

public interface ISimilarAudiosRepository
{
    ValueTask<ObservableDictionary<byte[], Similarity>> GetExistingMatchesForFileAsync(
        string collectionName, byte[] fileId, CancellationToken cancellationToken = default);

    ValueTask<IEnumerable<KeyValuePair<byte[], double>>> GetMatchingFingerprintsAsync(string collectionName,
        Fingerprint fingerprint, byte thresholdVotes, float gapAllowed, ICollection<byte[]> groupsAlreadyDone,
        CancellationToken cancellationToken = default);

    ValueTask<bool> LinkToSimilarFilesAsync(string collectionName, byte[] id,
        ICollection<Similarity> newSimilarities, CancellationToken cancellationToken = default);
}