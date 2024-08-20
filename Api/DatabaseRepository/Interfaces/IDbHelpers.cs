using Core.Entities;
using ObservableCollections;

namespace Api.DatabaseRepository.Interfaces;

public interface IDbHelpers
{
    Task<byte[]?> GetImageInfosAsync(HashKey id);

    Task<bool> CacheHashAsync(ImagesGroup group);

    Task<ObservableHashSet<HashKey>> GetSimilarImagesAlreadyDoneInRange(HashKey currentGroupId);
    
    Task<List<Similarity>> GetSimilarImages(HashKey currentGroupId, byte[] imageHash, int degreeOfSimilarity,
        IReadOnlyCollection<HashKey> groupsAlreadyDone);
    
    Task<bool> LinkToSimilarImagesAsync(HashKey id, ICollection<Similarity> newSimilarities);
}