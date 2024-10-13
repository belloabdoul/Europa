using System.Numerics;
using Sdcb.LibRaw;

namespace Core.Interfaces;

public interface IMainThumbnailGenerator
{
    ValueTask<bool> GenerateThumbnail<T>(ProcessedImage image, int width, int height, Span<T> pixels)
        where T : INumberBase<T>;
}