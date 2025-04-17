using System.Numerics;
using Core.Entities.Images;
using DotNext;

namespace Core.Interfaces.SimilarImages;

public interface IThumbnailGenerator
{
    Result<bool> GenerateThumbnail<T>(string imagePath, int width, int height, ColorSpace colorSpace, bool inPolarCoordinates,
        Span<T> thumbnail)  where T : struct, IBinaryFloatingPointIeee754<T>, IConvertible;
}