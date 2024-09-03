using Blake3;
using Core.Entities;
using ObservableCollections;

namespace Api.DatabaseRepository.Interfaces;

public interface IDbHelpers
{
    Task<byte[]?> GetImageInfosAsync(Hash id);

    Task<bool> CacheHashAsync(ImagesGroup group);

    Task<ObservableHashSet<Hash>> GetSimilarImagesAlreadyDoneInRange(Hash currentGroupId);
    
    Task<List<Similarity>> GetSimilarImages(Hash currentGroupId, byte[] imageHash, int degreeOfSimilarity,
        IReadOnlyCollection<Hash> groupsAlreadyDone);
    
    Task<bool> LinkToSimilarImagesAsync(Hash id, ICollection<Similarity> newSimilarities);
}