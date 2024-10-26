using System.Collections;
using Core.Entities;
using NSwag.Collections;

namespace Api.DatabaseRepository.Interfaces;

public interface IDbHelpers
{
    Task<BitArray?> GetImageInfos(byte[] id, PerceptualHashAlgorithm perceptualHashAlgorithm);

    Task<bool> InsertImageInfos(ImagesGroup group, PerceptualHashAlgorithm perceptualHashAlgorithm);

    ValueTask<bool> AddImageHash(ImagesGroup group, PerceptualHashAlgorithm perceptualHashAlgorithm);

    ValueTask DisableIndexing(CancellationToken cancellationToken);

    ValueTask EnableIndexing(CancellationToken cancellationToken);

    ValueTask<ObservableDictionary<byte[], byte>?> GetSimilarImagesAlreadyDoneInRange(byte[] currentGroupId,
        PerceptualHashAlgorithm perceptualHashAlgorithm);

    ValueTask<Similarity[]> GetSimilarImages(byte[] id, BitArray imageHash,
        PerceptualHashAlgorithm perceptualHashAlgorithm, int hashSize, int degreeOfSimilarity,
        ICollection<byte[]> groupsAlreadyDone);

    ValueTask<bool> LinkToSimilarImagesAsync(byte[] id, PerceptualHashAlgorithm perceptualHashAlgorithm,
        Similarity[] newSimilarities);
}