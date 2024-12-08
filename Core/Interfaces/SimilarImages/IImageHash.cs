using Core.Entities.Images;
using Core.Entities.SearchParameters;

namespace Core.Interfaces.SimilarImages;

public interface IImageHash
{
    public PerceptualHashAlgorithm PerceptualHashAlgorithm { get; }
    public int Width { get; }
    public int Height { get; }
    public int ImageSize { get; }
    public ColorSpace ColorSpace { get; }
    public int HashSize { get; }
    public Half[] GenerateHash(Span<float> pixels);
}