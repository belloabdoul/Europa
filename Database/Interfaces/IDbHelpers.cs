using Core.Entities;

// ReSharper disable ParameterTypeCanBeEnumerable.Global

namespace Database.Interfaces;

public interface IDbHelpers
{
    // Task<(long Id, Vector? ImageHash)> GetImageInfosAsync(byte[] hash, CancellationToken cancellationToken);

    Task<long> CacheHashAsync(ImagesGroup group, CancellationToken cancellationToken);

    Task<HashSet<long>> GetSimilarImagesGroupsAlreadyDoneInRange(long currentGroupId,
        double degreeOfSimilarity,
        CancellationToken cancellationToken);
    
    // Task<List<Similarity>> GetSimilarImagesGroups(long currentGroupId, Vector imageHash, double degreeOfSimilarity,
    //     ICollection<long> groupsAlreadyDone, CancellationToken cancellationToken);

    Task AddSimilarity(Similarity newSimilarities, CancellationToken cancellationToken);
}