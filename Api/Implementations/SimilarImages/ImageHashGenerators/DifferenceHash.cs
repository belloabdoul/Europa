using System.Runtime.Intrinsics;
using Core.Interfaces;

namespace Api.Implementations.SimilarImages.ImageHashGenerators;

public class DifferenceHash : IImageHash
{
    private const int Width = 9;
    private const int Height = 8;
    private static readonly Vector64<byte> VectorOfZeroes = Vector64.Create<byte>(0);
    private static readonly Vector64<byte> VectorOfOnes = Vector64.Create<byte>(1);

    public int GetRequiredWidth() => Width;

    public int GetRequiredHeight() => Height;
    
    public byte[] GenerateHash(byte[] pixels)
    {
        var hash = new byte[(Width - 1) * Height];

        for (var y = 0; y < Height; y++)
        {
            var firstEight = Vector64.Create<byte>(pixels.AsSpan(y * Width, Width - 1));
            var lastEight = Vector64.Create<byte>(pixels.AsSpan(y * Width + 1, Width - 1));
            var condition = Vector64.LessThan(firstEight, lastEight);

            Vector64.ConditionalSelect(condition, VectorOfOnes, VectorOfZeroes).CopyTo(hash.AsSpan(y * Height, Height));
        }

        return hash;
    }
}