using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Core.Entities;
using Core.Interfaces;
using DotNext.Buffers;

namespace Api.Implementations.SimilarImages.ImageHashGenerators;

public class DifferenceHash : IImageHash
{
    private const int Width = 9;
    private const int Height = 8;
    private const int ImageSize = Width * Height;
    public static int HashSize => (Width - 1) * Height;

    public PerceptualHashAlgorithm PerceptualHashAlgorithm => PerceptualHashAlgorithm.DifferenceHash;

    [SkipLocalsInit]
    public async ValueTask<byte[]> GenerateHash(string imagePath, IThumbnailGenerator thumbnailGenerator)
    {
        using var pixels = new MemoryOwner<byte>(ArrayPool<byte>.Shared, ImageSize);
        await thumbnailGenerator.GenerateThumbnail(imagePath, Width, Height, pixels.Span);

        var hash = new byte[(Width - 1) * Height];
        CompareLessThanOrEqual(pixels.Span, hash);
        return hash;
    }

    private static void CompareLessThanOrEqual(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        ref var sourceRef = ref MemoryMarshal.GetReference(source);
        ref var destinationRef = ref MemoryMarshal.GetReference(destination);

        for (nuint y = 0; y < Height; ++y)
        {
            var rowStart = y * (Width - 1);
            Vector64.ConditionalSelect(Vector64.LessThan(
                        Unsafe.As<byte, Vector64<byte>>(ref Unsafe.Add(ref sourceRef, rowStart)),
                        Unsafe.As<byte, Vector64<byte>>(ref Unsafe.Add(ref sourceRef, rowStart + 1))),
                    Vector64<byte>.One,
                    Vector64<byte>.Zero)
                .CopyTo(MemoryMarshal.CreateSpan(ref Unsafe.Add(ref destinationRef, rowStart), 8));
        }
    }
}