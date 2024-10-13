using System.Numerics;
using Core.Entities;

namespace Core.Interfaces;

public interface IImageHash
{
    ValueTask<byte[]> GenerateHash(string imagePath, IThumbnailGenerator thumbnailGenerator);
    PerceptualHashAlgorithm PerceptualHashAlgorithm { get; }
}