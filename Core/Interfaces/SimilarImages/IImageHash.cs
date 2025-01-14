using Core.Entities.Images;

namespace Core.Interfaces.SimilarImages;

public interface IImageHash
{
    public int Width { get; }
    public int Height { get; }
    public int ImageSize { get; }
    public ColorSpace ColorSpace { get; }
    public Half[] GenerateHash(Span<float> pixels);
}