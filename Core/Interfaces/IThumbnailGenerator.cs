namespace Core.Interfaces;

public interface IThumbnailGenerator
{
    bool GenerateThumbnail(string imagePath, int width, int height, Span<byte> pixels);
}