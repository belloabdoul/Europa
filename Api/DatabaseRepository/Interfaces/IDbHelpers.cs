using Core.Entities;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Api.DatabaseRepository.Interfaces;

public interface IDbHelpers
{
    ValueTask<Half[]?> GetImageInfosAsync(string id);

    Task<bool> CacheHashAsync(ImagesGroup group);

    Task<ObservableHashSet<string>> GetSimilarImagesAlreadyDoneInRange(string currentGroupId);

    Task<List<Similarity>> GetSimilarImages<T>(string id, T[] imageHash,
        int degreeOfSimilarity, IReadOnlyCollection<string> groupsAlreadyDone) where T : struct;

    Task<bool> LinkToSimilarImagesAsync(string id, ICollection<Similarity> newSimilarities);
}