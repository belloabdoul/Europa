using Blake3;
using Core.Entities;
using Redis.OM;

// ReSharper disable ParameterTypeCanBeEnumerable.Global

namespace Database.Interfaces;

public interface IDbHelpers
{
    Task<Vector<byte[]>?> GetImageInfosAsync(string id);

    Task CacheHashAsync(ImagesGroup group);

    Task<HashSet<string>> GetSimilarImagesAlreadyDoneInRange(string currentGroupId);
    
    Task<List<Similarity>> GetSimilarImages(string currentGroupId, Vector<byte[]> imageHash, double degreeOfSimilarity,
        ICollection<string> groupsAlreadyDone);

    Task LinkToSimilariImagesAsync(string id, ICollection<Similarity> newSimilarities, bool isEmpty);
}