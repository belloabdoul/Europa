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
    public int HashSize => 64;

    public PerceptualHashAlgorithm PerceptualHashAlgorithm => PerceptualHashAlgorithm.DifferenceHash;

    [SkipLocalsInit]
    public async ValueTask<float[]> GenerateHash(string imagePath, IThumbnailGenerator thumbnailGenerator)
    {
        using var pixels = new MemoryOwner<float>(ArrayPool<float>.Shared, ImageSize);
        await thumbnailGenerator.GenerateThumbnail(imagePath, Width, Height, pixels.Span);

        var hash = new float[(Width - 1) * Height];
        CompareLessThanOrEqual(pixels.Span, hash);
        return hash;
    }

    private static void CompareLessThanOrEqual(ReadOnlySpan<float> source, Span<float> destination)
    {
        ref var sourceRef = ref MemoryMarshal.GetReference(source);
        ref var destinationRef = ref MemoryMarshal.GetReference(destination);

        for (nuint y = 0; y < Height; ++y)
        {
            Vector256.ConditionalSelect(Vector256.LessThan(
                        Unsafe.As<float, Vector256<float>>(ref Unsafe.Add(ref sourceRef, y * Width)),
                        Unsafe.As<float, Vector256<float>>(ref Unsafe.Add(ref sourceRef, y * Width + 1))),
                    Vector256<float>.One,
                    -Vector256<float>.One)
                .CopyTo(MemoryMarshal.CreateSpan(ref Unsafe.Add(ref destinationRef, y * (Width - 1)), 8));
        }
    }
}