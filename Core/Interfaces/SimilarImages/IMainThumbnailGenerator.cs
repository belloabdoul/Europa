using System.Numerics;
using Core.Entities.Images;
using DotNext;
using Sdcb.LibRaw;

namespace Core.Interfaces.SimilarImages;

public interface IMainThumbnailGenerator
{
    Result<bool> GenerateThumbnail<T>(ProcessedImage image, int width, int height, ColorSpace colorSpace,
        bool inPolarCoordinates, Span<T> thumbnail) where T : struct, IBinaryFloatingPointIeee754<T>, IConvertible;
}