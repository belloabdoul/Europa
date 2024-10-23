using Core.Entities;

namespace Core.Interfaces;

public interface IThumbnailGenerator
{
    ValueTask<bool> GenerateThumbnail(string imagePath, int width, int height, Span<byte> pixels);

    FileType AssociatedImageType { get; }
}