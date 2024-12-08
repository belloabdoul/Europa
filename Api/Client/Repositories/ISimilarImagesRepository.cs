using Core.Entities.Images;
using Core.Entities.SearchParameters;
using NSwag.Collections;

namespace Api.Client.Repositories;

public interface ISimilarImagesRepository
{
    ValueTask<ObservableDictionary<byte[], Similarity>?> GetExistingSimilaritiesForImage(string collectionName,
        byte[] currentGroupId, PerceptualHashAlgorithm perceptualHashAlgorithm);

    ValueTask<IEnumerable<KeyValuePair<byte[], Similarity>>> GetSimilarImages(string collectionName, byte[] id,
        ReadOnlyMemory<Half> imageHash, PerceptualHashAlgorithm perceptualHashAlgorithm, decimal degreeOfSimilarity,
        ICollection<byte[]> groupsAlreadyDone);

    ValueTask<bool> LinkToSimilarImagesAsync(string collectionName, byte[] id,
        PerceptualHashAlgorithm perceptualHashAlgorithm, ICollection<Similarity> newSimilarities);
}