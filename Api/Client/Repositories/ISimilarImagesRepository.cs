using System.Collections;
using Core.Entities.Images;
using Core.Entities.SearchParameters;
using NSwag.Collections;

namespace Api.Client.Repositories;

public interface ISimilarImagesRepository
{
    ValueTask<ObservableDictionary<byte[], byte>?> GetSimilarImagesAlreadyDoneInRange(byte[] currentGroupId,
        PerceptualHashAlgorithm perceptualHashAlgorithm);

    ValueTask<Similarity[]> GetSimilarImages(byte[] id, BitArray imageHash,
        PerceptualHashAlgorithm perceptualHashAlgorithm, int hashSize, int degreeOfSimilarity,
        ICollection<byte[]> groupsAlreadyDone);

    ValueTask<bool> LinkToSimilarImagesAsync(byte[] id, PerceptualHashAlgorithm perceptualHashAlgorithm,
        Similarity[] newSimilarities);
}