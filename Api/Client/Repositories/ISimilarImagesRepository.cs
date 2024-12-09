using Core.Entities.Images;
using Core.Entities.SearchParameters;
using NSwag.Collections;

namespace Api.Client.Repositories;

public interface ISimilarImagesRepository
{
    ValueTask<ObservableDictionary<byte[], Similarity>?> GetExistingSimilaritiesForImage(string collectionName,
        byte[] currentGroupId, PerceptualHashAlgorithm perceptualHashAlgorithm,
        CancellationToken cancellationToken = default);

    ValueTask<IEnumerable<KeyValuePair<byte[], Similarity>>> GetSimilarImages(string collectionName, byte[] id,
        ReadOnlyMemory<Half> imageHash, PerceptualHashAlgorithm perceptualHashAlgorithm, decimal degreeOfSimilarity,
        ICollection<byte[]> groupsAlreadyDone, CancellationToken cancellationToken = default);

    ValueTask<bool> LinkToSimilarImagesAsync(string collectionName, Guid id,
        PerceptualHashAlgorithm perceptualHashAlgorithm, ICollection<Similarity> newSimilarities,
        CancellationToken cancellationToken = default);
}