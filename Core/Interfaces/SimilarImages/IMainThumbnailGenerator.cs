using Sdcb.LibRaw;

namespace Core.Interfaces.SimilarImages;

public interface IMainThumbnailGenerator
{
    ValueTask<bool> GenerateThumbnail(ProcessedImage image, int width, int height, Span<byte> pixels);
}