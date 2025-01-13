using Core.Entities.Audios;

namespace Api.Client.Repositories;

public interface IAudioInfosRepository
{
    ValueTask<int> GetFingerprintsCount(string collectionName, byte[] id, CancellationToken cancellationToken = default);

    ValueTask<int> InsertFingerprintsAsync(string collectionName, IList<Fingerprint> group,
        CancellationToken cancellationToken = default);
}