﻿using System.Collections;
using Core.Entities.Images;
using Core.Entities.SearchParameters;
using NSwag.Collections;

namespace Api.Client.Repositories;

public interface ISimilarImagesRepository
{
    ValueTask<ObservableDictionary<byte[], Similarity>?> GetExistingSimilaritiesForImage(
        byte[] currentGroupId, PerceptualHashAlgorithm perceptualHashAlgorithm);

    ValueTask<IEnumerable<KeyValuePair<byte[], Similarity>>> GetSimilarImages(byte[] id, Half[] imageHash,
        PerceptualHashAlgorithm perceptualHashAlgorithm, int hashSize, int degreeOfSimilarity,
        ICollection<byte[]> groupsAlreadyDone);

    ValueTask<bool> LinkToSimilarImagesAsync(byte[] id, PerceptualHashAlgorithm perceptualHashAlgorithm,
        ICollection<Similarity> newSimilarities);
}