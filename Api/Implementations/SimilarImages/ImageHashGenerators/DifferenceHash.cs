using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using CommunityToolkit.HighPerformance.Buffers;
using Core.Entities.SearchParameters;
using Core.Interfaces.SimilarImages;

namespace Api.Implementations.SimilarImages.ImageHashGenerators;

public class DifferenceHash : IImageHash
{
    public int Width => 9;
    public int Height => 8;
    public int ImageSize => Width * Height;
    public int HashSize => Height * (Width - 1);

    public PerceptualHashAlgorithm PerceptualHashAlgorithm => PerceptualHashAlgorithm.DifferenceHash;

    public BitArray GenerateHash(ReadOnlySpan<byte> pixels)
    {
        if (pixels.Length != ImageSize)
            throw new ArgumentException(
                $"The pixel array is not of the size {ImageSize} required for perceptual hashing.");

        using var hash = MemoryOwner<byte>.Allocate(HashSize);
        CompareLessThanOrEqual(pixels, hash.Span);
        return new BitArray(Unsafe.BitCast<Span<byte>, Span<bool>>(hash.Span).ToArray());
    }

    private void CompareLessThanOrEqual(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        ref var sourceRef = ref MemoryMarshal.GetReference(source);
        ref var destinationRef = ref MemoryMarshal.GetReference(destination);

        for (nuint y = 0; y < (nuint)Height; ++y)
        {
            var rowStart = y * (nuint)(Width - 1);
            Vector64.ConditionalSelect(Vector64.LessThan(
                        Unsafe.As<byte, Vector64<byte>>(ref Unsafe.Add(ref sourceRef, rowStart)),
                        Unsafe.As<byte, Vector64<byte>>(ref Unsafe.Add(ref sourceRef, rowStart + 1))),
                    Vector64<byte>.One,
                    Vector64<byte>.Zero)
                .CopyTo(MemoryMarshal.CreateSpan(ref Unsafe.Add(ref destinationRef, rowStart), 8));
        }
    }
}