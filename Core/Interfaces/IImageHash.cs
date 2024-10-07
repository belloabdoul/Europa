using Core.Entities;

namespace Core.Interfaces;

public interface IImageHash
{
    ValueTask<Half[]> GenerateHash(ReadOnlySpan<byte> pixels);
    PerceptualHashAlgorithm PerceptualHashAlgorithm { get; }
    int RequiredWidth { get; }
    int RequiredHeight { get; }
}