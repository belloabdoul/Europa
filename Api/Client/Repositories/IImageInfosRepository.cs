using Core.Entities.Images;

namespace Api.Client.Repositories;

public interface IImageInfosRepository
{
    ValueTask<ReadOnlyMemory<Half>> GetImageHash(string collectionName, byte[] id, CancellationToken cancellationToken = default);

    ValueTask<bool> InsertImageInfos(string collectionName, ImagesGroup group,
        CancellationToken cancellationToken = default);
}