using System.Runtime.CompilerServices;
using Core.Interfaces;

namespace Api.Implementations.SimilarImages.ImageHashGenerators;

public class DifferenceHash : IImageHash
{
    private const int Width = 9;
    private const int Height = 8;

    public static int GetRequiredWidth() => Width;

    public static int GetRequiredHeight() => Height;

    [SkipLocalsInit]
    public Half[] GenerateHash(ReadOnlySpan<byte> pixels)
    {
        var hash = new Half[(Width - 1) * Height];

        for (var y = 0; y < Height; y++)
        for (var x = 0; x < Width - 1; x++)
            hash[y * Height + x] = pixels[y * Width + x] < pixels[y * Width + x + 1] ? Half.One : Half.Zero;
        return hash;
    }
}