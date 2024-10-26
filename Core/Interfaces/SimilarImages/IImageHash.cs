using System.Collections;
using Core.Entities;
using Core.Entities.Images;
using Core.Entities.SearchParameters;

namespace Core.Interfaces.SimilarImages;

public interface IImageHash
{
    ValueTask<BitArray> GenerateHash(string imagePath, IThumbnailGenerator thumbnailGenerator);
    PerceptualHashAlgorithm PerceptualHashAlgorithm { get; }
    int HashSize { get; }
}