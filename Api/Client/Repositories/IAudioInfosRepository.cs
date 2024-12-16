using Core.Entities.Audios;

namespace Api.Client.Repositories;

public interface IAudioInfosRepository
{
    ValueTask<bool> IsAlreadyInsertedAsync(string collectionName, byte[] id, int estimatedNumberOfFingerprints,
        CancellationToken cancellationToken = default);

    ValueTask<bool> InsertFingerprintsAsync(string collectionName, List<Fingerprint> group,
        CancellationToken cancellationToken = default);
}