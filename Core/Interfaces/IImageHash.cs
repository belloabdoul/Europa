using Core.Entities;

namespace Core.Interfaces;

public interface IImageHash
{
    Half[] GenerateHash(ReadOnlySpan<byte> pixels);
    PerceptualHashAlgorithm GetPerceptualHashAlgorithm();
}