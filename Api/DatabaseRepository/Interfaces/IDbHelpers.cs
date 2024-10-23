using System.Collections;
using Core.Entities;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Api.DatabaseRepository.Interfaces;

public interface IDbHelpers
{
    ValueTask<(Guid? Uuid, BitArray? ImageHash)> GetImageInfos(byte[] id,
        PerceptualHashAlgorithm perceptualHashAlgorithm);

    ValueTask<bool> InsertImageInfos(ImagesGroup group, PerceptualHashAlgorithm perceptualHashAlgorithm);

    ValueTask<bool> AddImageHash(Guid uuid, ImagesGroup group, PerceptualHashAlgorithm perceptualHashAlgorithm);

    ValueTask DisableIndexing(CancellationToken cancellationToken);

    ValueTask EnableIndexing(CancellationToken cancellationToken);

    ValueTask<ObservableHashSet<byte[]>?> GetSimilarImagesAlreadyDoneInRange(byte[] currentGroupId,
        PerceptualHashAlgorithm perceptualHashAlgorithm);

    ValueTask<Similarity[]> GetSimilarImages(byte[] id, BitArray imageHash,
        PerceptualHashAlgorithm perceptualHashAlgorithm, int hashSize, int degreeOfSimilarity,
        IReadOnlyCollection<byte[]> groupsAlreadyDone);

    ValueTask<bool> LinkToSimilarImagesAsync(byte[] id, PerceptualHashAlgorithm perceptualHashAlgorithm,
        Similarity[] newSimilarities);
}