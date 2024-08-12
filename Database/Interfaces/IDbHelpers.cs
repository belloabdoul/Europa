using Core.Entities;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Redis.OM;

namespace Database.Interfaces;

public interface IDbHelpers
{
    Task<Vector<byte[]>?> GetImageInfosAsync(string id);

    Task CacheHashAsync(ImagesGroup group);

    Task<ObservableHashSet<string>> GetSimilarImagesAlreadyDoneInRange(string currentGroupId);
    
    Task<List<Similarity>> GetSimilarImages(string currentGroupId, Vector<byte[]> imageHash, int degreeOfSimilarity,
        ICollection<string> groupsAlreadyDone);

    Task LinkToSimilarImagesAsync(string id, ICollection<Similarity> newSimilarities, bool isEmpty);
}