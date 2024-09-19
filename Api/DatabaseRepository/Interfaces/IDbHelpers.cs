using Core.Entities;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Api.DatabaseRepository.Interfaces;

public interface IDbHelpers
{
    ValueTask<Half[]?> GetImageInfosAsync(string id, PerceptualHashAlgorithm perceptualHashAlgorithm);

    Task<bool> CacheHashAsync(ImagesGroup group, PerceptualHashAlgorithm perceptualHashAlgorithm);

    Task<ObservableHashSet<string>> GetSimilarImagesAlreadyDoneInRange(string currentGroupId,
        PerceptualHashAlgorithm perceptualHashAlgorithm);

    Task<List<Similarity>> GetSimilarImages<T>(string id, T[] imageHash,
        PerceptualHashAlgorithm perceptualHashAlgorithm, int degreeOfSimilarity,
        IReadOnlyCollection<string> groupsAlreadyDone) where T : struct;

    Task<bool> LinkToSimilarImagesAsync(string id, PerceptualHashAlgorithm perceptualHashAlgorithm,
        ICollection<Similarity> newSimilarities);
}