using System.Numerics;
using Core.Entities;

namespace Core.Interfaces;

public interface IImageHash
{
    ValueTask<float[]> GenerateHash(string imagePath, IThumbnailGenerator thumbnailGenerator);
    PerceptualHashAlgorithm PerceptualHashAlgorithm { get; }
    int HashSize { get; }
}