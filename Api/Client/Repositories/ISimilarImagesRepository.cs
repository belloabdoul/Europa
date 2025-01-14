using Core.Entities.Commons;
using Swordfish.NET.Collections;

namespace Api.Client.Repositories;

public interface ISimilarImagesRepository
{
    ValueTask<ConcurrentObservableDictionary<byte[], Similarity>> GetExistingSimilaritiesForImage(string collectionName,
        byte[] currentGroupId, CancellationToken cancellationToken = default);

    ValueTask<IEnumerable<KeyValuePair<byte[], Similarity>>> GetSimilarImages(string collectionName, byte[] id,
        ReadOnlyMemory<Half> imageHash, decimal degreeOfSimilarity, ICollection<byte[]> groupsAlreadyDone,
        CancellationToken cancellationToken = default);

    ValueTask<bool> LinkToSimilarImagesAsync(string collectionName, byte[] id, ICollection<Similarity> newSimilarities,
        CancellationToken cancellationToken = default);
}