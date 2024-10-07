using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Core.Entities;
using Core.Interfaces;

namespace Api.Implementations.SimilarImages.ImageHashGenerators;

public class DifferenceHash : IImageHash
{
    private const int Width = 9;
    private const int Height = 8;

    public int RequiredWidth => Width;

    public int RequiredHeight => Height;

    public PerceptualHashAlgorithm PerceptualHashAlgorithm => PerceptualHashAlgorithm.DifferenceHash;

    [SkipLocalsInit]
    public ValueTask<Half[]> GenerateHash(ReadOnlySpan<byte> pixels)
    {
        var hash = new Half[(Width - 1) * Height];
        ref var pixel = ref MemoryMarshal.GetReference(pixels);
        ref var hashRef = ref MemoryMarshal.GetReference(hash.AsSpan());

        for (nint y = 0; y < Height; y++)
        {
            Unsafe.Add(ref hashRef, y * Height) =
                Unsafe.Add(ref pixel, y * Width) < Unsafe.Add(ref pixel, y * Width + 1) ? Half.Zero : Half.One;
            Unsafe.Add(ref hashRef, y * Height + 1) =
                Unsafe.Add(ref pixel, y * Width + 1) < Unsafe.Add(ref pixel, y * Width + 2) ? Half.Zero : Half.One;
            Unsafe.Add(ref hashRef, y * Height + 2) =
                Unsafe.Add(ref pixel, y * Width + 2) < Unsafe.Add(ref pixel, y * Width + 3) ? Half.Zero : Half.One;
            Unsafe.Add(ref hashRef, y * Height + 3) =
                Unsafe.Add(ref pixel, y * Width + 3) < Unsafe.Add(ref pixel, y * Width + 4) ? Half.Zero : Half.One;
            Unsafe.Add(ref hashRef, y * Height + 4) =
                Unsafe.Add(ref pixel, y * Width + 4) < Unsafe.Add(ref pixel, y * Width + 5) ? Half.Zero : Half.One;
            Unsafe.Add(ref hashRef, y * Height + 5) =
                Unsafe.Add(ref pixel, y * Width + 5) < Unsafe.Add(ref pixel, y * Width + 6) ? Half.Zero : Half.One;
            Unsafe.Add(ref hashRef, y * Height + 6) =
                Unsafe.Add(ref pixel, y * Width + 6) < Unsafe.Add(ref pixel, y * Width + 7) ? Half.Zero : Half.One;
            Unsafe.Add(ref hashRef, y * Height + 7) =
                Unsafe.Add(ref pixel, y * Width + 7) < Unsafe.Add(ref pixel, y * Width + 8) ? Half.Zero : Half.One;
        }

        return ValueTask.FromResult(hash);
    }
}