using Core.Entities;
using ObservableCollections;
using Redis.OM;

namespace Database.Interfaces;

public interface IDbHelpers
{
    Task<Vector<byte[]>?> GetImageInfosAsync(HashKey id);

    Task CacheHashAsync(ImagesGroup group);

    Task<ObservableHashSet<HashKey>> GetSimilarImagesAlreadyDoneInRange(HashKey currentGroupId);

    Task<List<Similarity>> GetSimilarImages(HashKey currentGroupId, Vector<byte[]> imageHash, int degreeOfSimilarity,
        IReadOnlyCollection<HashKey> groupsAlreadyDone);

    Task LinkToSimilarImagesAsync(HashKey id, ICollection<Similarity> newSimilarities, bool isEmpty);
}