using Core.Entities;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using U8;

namespace Api.DatabaseRepository.Interfaces;

public interface IDbHelpers
{
    ValueTask<Half[]?> GetImageInfos(U8String id, PerceptualHashAlgorithm perceptualHashAlgorithm);

    Task<bool> CacheHash(ImagesGroup group, PerceptualHashAlgorithm perceptualHashAlgorithm);

    Task<ObservableHashSet<U8String>> GetSimilarImagesAlreadyDoneInRange(U8String currentGroupId,
        PerceptualHashAlgorithm perceptualHashAlgorithm);

    Task<List<Similarity>> GetSimilarImages<T>(U8String id, T[] imageHash,
        PerceptualHashAlgorithm perceptualHashAlgorithm, int degreeOfSimilarity,
        IReadOnlyCollection<U8String> groupsAlreadyDone) where T : struct;

    Task<bool> LinkToSimilarImagesAsync(U8String id, PerceptualHashAlgorithm perceptualHashAlgorithm,
        ICollection<Similarity> newSimilarities);
}