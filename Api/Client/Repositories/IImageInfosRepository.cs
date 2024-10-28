using System.Collections;
using Core.Entities.Images;
using Core.Entities.SearchParameters;

namespace Api.Client.Repositories;

public interface IImageInfosRepository
{
    ValueTask<BitArray?> GetImageInfos(byte[] id, PerceptualHashAlgorithm perceptualHashAlgorithm);

    ValueTask<bool> InsertImageInfos(ImagesGroup group, PerceptualHashAlgorithm perceptualHashAlgorithm);

    ValueTask<bool> AddImageHash(ImagesGroup group, PerceptualHashAlgorithm perceptualHashAlgorithm);
}