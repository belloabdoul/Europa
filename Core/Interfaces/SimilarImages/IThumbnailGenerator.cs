using Core.Entities.Images;

namespace Core.Interfaces.SimilarImages;

public interface IThumbnailGenerator
{
    bool GenerateThumbnail(string imagePath, int width, int height, Span<float> pixels, ColorSpace colorSpace);
}