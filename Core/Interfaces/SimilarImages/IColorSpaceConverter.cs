namespace Core.Interfaces.SimilarImages;

public interface IColorSpaceConverter
{
    bool GetPixelsInLabColorSpace(ReadOnlyMemory<byte> pixels, int width, int height, Span<float> pixelsLabs);
}