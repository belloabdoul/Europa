using Sdcb.LibRaw;

namespace Core.Interfaces;

public interface IMainThumbnailGenerator
{
    byte[] GenerateThumbnail(ProcessedImage image, int width, int height);
}