using Core.Entities.Images;

namespace Api.Client.Repositories;

public interface IImageInfosRepository
{
    ValueTask<ImageInfos> GetImageInfos(byte[] id, CancellationToken cancellationToken = default);

    ValueTask<long> InsertImageInfos(ImagesGroup group, CancellationToken cancellationToken = default);
}