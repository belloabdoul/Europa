using Core.Entities.Images;
using Core.Entities.SearchParameters;

namespace Api.Client.Repositories;

public interface IImageInfosRepository
{
    ValueTask<Half[]?> GetImageInfos(byte[] id, PerceptualHashAlgorithm perceptualHashAlgorithm);

    ValueTask<bool> InsertImageInfos(ImagesGroup group, PerceptualHashAlgorithm perceptualHashAlgorithm);

    ValueTask<bool> AddImageHash(ImagesGroup group, PerceptualHashAlgorithm perceptualHashAlgorithm);
}