using System.Collections;
using Core.Entities.SearchParameters;

namespace Core.Interfaces.SimilarImages;

public interface IImageHash
{
    public PerceptualHashAlgorithm PerceptualHashAlgorithm { get; }
    public int Width { get; }
    public int Height { get; }
    public int ImageSize { get; }
    public int HashSize { get; }
    public BitArray GenerateHash(ReadOnlySpan<byte> pixels);
}