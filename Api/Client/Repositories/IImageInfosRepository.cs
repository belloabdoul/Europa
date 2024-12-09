using Core.Entities.Images;
using Core.Entities.SearchParameters;

namespace Api.Client.Repositories;

public interface IImageInfosRepository
{
    ValueTask<ImageInfos> GetImageInfos(string collectionName, byte[] id,
        PerceptualHashAlgorithm perceptualHashAlgorithm, CancellationToken cancellationToken = default);

    ValueTask<bool> InsertImageInfos(string collectionName, List<ImagesGroup> group,
        PerceptualHashAlgorithm perceptualHashAlgorithm, CancellationToken cancellationToken = default);
}