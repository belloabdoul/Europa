using System.Numerics;
using Core.Entities.Images;

namespace Core.Interfaces.SimilarImages;

public interface IPolarCoordinatesConverter
{
    public bool ConvertToPolarCoordinates<T>(ReadOnlySpan<byte> input, Span<T> output, int width, int height,
        ColorSpace colorSpace) where T : struct, IBinaryFloatingPointIeee754<T>, IConvertible;
}