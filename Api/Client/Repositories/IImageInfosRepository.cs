using Core.Entities.Images;
using Core.Entities.SearchParameters;

namespace Api.Client.Repositories;

public interface IImageInfosRepository
{
    ValueTask<ReadOnlyMemory<Half>?> GetImageInfos(string collectionName, byte[] id, PerceptualHashAlgorithm perceptualHashAlgorithm);

    ValueTask<bool> InsertImageInfos(string collectionName, List<ImagesGroup> group, PerceptualHashAlgorithm perceptualHashAlgorithm);
}