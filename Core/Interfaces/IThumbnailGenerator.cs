using System.Numerics;
using Core.Entities;

namespace Core.Interfaces;

public interface IThumbnailGenerator
{
    ValueTask<bool> GenerateThumbnail<T>(string imagePath, int width, int height, Span<T> pixels)
        where T : INumberBase<T>;

    FileType AssociatedImageType { get; }
}