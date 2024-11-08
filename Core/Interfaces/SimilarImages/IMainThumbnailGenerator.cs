using Core.Entities.Images;
using Sdcb.LibRaw;

namespace Core.Interfaces.SimilarImages;

public interface IMainThumbnailGenerator
{
    bool GenerateThumbnail(ProcessedImage image, int width, int height, Span<float> pixels, ColorSpace colorSpace);
}