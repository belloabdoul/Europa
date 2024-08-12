namespace Core.Interfaces;

public interface IThumbnailGenerator
{
    byte[] GenerateThumbnail(string imagePath, int width, int height);
}