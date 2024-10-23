using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using CommunityToolkit.HighPerformance.Buffers;
using Core.Entities;
using Core.Interfaces;

namespace Api.Implementations.SimilarImages.ImageHashGenerators;

public class DifferenceHash : IImageHash
{
    private const int Width = 9;
    private const int Height = 8;
    private const int ImageSize = Width * Height;
    public int HashSize => Height * (Width - 1);

    public PerceptualHashAlgorithm PerceptualHashAlgorithm => PerceptualHashAlgorithm.DifferenceHash;

    public async ValueTask<BitArray> GenerateHash(string imagePath, IThumbnailGenerator thumbnailGenerator)
    {
        using var pixels = MemoryOwner<byte>.Allocate(ImageSize);
        await thumbnailGenerator.GenerateThumbnail(imagePath, Width, Height, pixels.Span);

        using var hash = MemoryOwner<byte>.Allocate(HashSize);
        CompareLessThanOrEqual(pixels.Span, hash.Span);
        return new BitArray(Unsafe.BitCast<Span<byte>, Span<bool>>(hash.Span).ToArray());
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