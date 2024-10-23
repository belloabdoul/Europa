using System.Collections;
using Core.Entities;

namespace Core.Interfaces;

public interface IImageHash
{
    ValueTask<BitArray> GenerateHash(string imagePath, IThumbnailGenerator thumbnailGenerator);
    PerceptualHashAlgorithm PerceptualHashAlgorithm { get; }
    int HashSize { get; }
}