using Core.Entities;
using Core.Entities.Files;

namespace Core.Interfaces.SimilarImages;

public interface IThumbnailGenerator
{
    ValueTask<bool> GenerateThumbnail(string imagePath, int width, int height, Span<byte> pixels);

    FileType AssociatedImageType { get; }
}