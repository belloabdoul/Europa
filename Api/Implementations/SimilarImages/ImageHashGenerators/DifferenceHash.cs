using Core.Interfaces;

namespace Api.Implementations.SimilarImages.ImageHashGenerators;

public class DifferenceHash : IImageHash
{
    private const int Width = 9;
    private const int Height = 8;

    public int GetRequiredWidth() => Width;

    public int GetRequiredHeight() => Height;

    public byte[] GenerateHash(ReadOnlySpan<byte> pixels)
    {
        var hash = new byte[(Width - 1) * Height];
        
        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width - 1; x++)
            if (pixels[y * Width + x] < pixels[y * Width + x + 1])
                hash[y * Height + x] = 1;
        
        return hash;
    }
}