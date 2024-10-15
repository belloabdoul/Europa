using Core.Entities;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using U8;

namespace Api.DatabaseRepository.Interfaces;

public interface IDbHelpers
{
    ValueTask<byte[]?> GetImageInfos(U8String id, PerceptualHashAlgorithm perceptualHashAlgorithm);

    ValueTask<bool> CacheHash(ImagesGroup group, PerceptualHashAlgorithm perceptualHashAlgorithm);

    ValueTask<ObservableHashSet<U8String>?> GetSimilarImagesAlreadyDoneInRange(U8String currentGroupId,
        PerceptualHashAlgorithm perceptualHashAlgorithm);

    Task<List<Similarity>> GetSimilarImages(U8String id, byte[] imageHash,
        PerceptualHashAlgorithm perceptualHashAlgorithm, int degreeOfSimilarity,
        IReadOnlyCollection<U8String> groupsAlreadyDone);

    Task<bool> LinkToSimilarImagesAsync(U8String id, PerceptualHashAlgorithm perceptualHashAlgorithm,
        ICollection<Similarity> newSimilarities);
}