using System.Collections;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance;
using CommunityToolkit.HighPerformance.Buffers;
using Core.Entities.SearchParameters;
using Core.Interfaces.SimilarImages;

namespace Api.Implementations.SimilarImages.ImageHashGenerators;

// Block mean hash
public class BlockMeanHash : IImageHash
{
    private const int HeightOrWidthSize = 256;
    private const int BlockSize = 16;
    private const int BlockPerRowOrCol = HeightOrWidthSize / BlockSize;
    private const int LastRowOrColSize = HeightOrWidthSize - BlockSize;
    private readonly int _pixelRowOrColStep;
    public int HashSize { get; }
    public int ImageSize => HeightOrWidthSize * HeightOrWidthSize;
    public int Height => HeightOrWidthSize;
    public int Width => HeightOrWidthSize;

    public BlockMeanHash()
    {
        HashSize = BlockPerRowOrCol * BlockPerRowOrCol;
        _pixelRowOrColStep = BlockSize;
    }

    public BlockMeanHash(bool mode1)
    {
        _pixelRowOrColStep = BlockSize;
        if (mode1)
        {
            HashSize = (BlockPerRowOrCol * 2 - 1) * (BlockPerRowOrCol * 2 - 1);
            _pixelRowOrColStep /= 2;
        }
        else
            HashSize = BlockPerRowOrCol * BlockPerRowOrCol;
    }

    public PerceptualHashAlgorithm PerceptualHashAlgorithm => PerceptualHashAlgorithm.BlockMeanHash;

    [SkipLocalsInit]
    public BitArray GenerateHash(ReadOnlySpan<byte> pixels)
    {
        if (pixels.Length != ImageSize)
            throw new ArgumentException(
                $"The pixel array is not of the size {ImageSize} required for perceptual hashing.");
        using var pixelsAsInts = MemoryOwner<int>.Allocate(ImageSize);

        TensorPrimitives.ConvertChecked(pixels, pixelsAsInts.Span);

        using var mean = MemoryOwner<double>.Allocate(HashSize);
        FindMean(pixelsAsInts.Span, mean.Span);
        return CreateHash(pixelsAsInts.Span, mean.Span);
    }

    [SkipLocalsInit]
    private BitArray CreateHash(ReadOnlySpan<int> pixels, Span<double> means)
    {
        var median = TensorPrimitives.Sum(pixels) / (double)(ImageSize);

        var hash = new BitArray(means.Length);
        ref var meansReference = ref MemoryMarshal.GetReference(means);
        for (nint i = 0; i < means.Length; i++)
        {
            if (Unsafe.Add(ref meansReference, i) < median)
                continue;
            hash[(int)i] = true;
        }

        return hash;
    }

    [SkipLocalsInit]
    private void FindMean(ReadOnlySpan<int> pixels, Span<double> means)
    {
        nint blockIdx = 0;
        var pixels2D = pixels.AsSpan2D(HeightOrWidthSize, HeightOrWidthSize);
        ref var meansReference = ref MemoryMarshal.GetReference(means);

        for (var row = 0; row < LastRowOrColSize; row += _pixelRowOrColStep)
        {
            for (var col = 0; col < LastRowOrColSize; col += _pixelRowOrColStep)
            {
                Unsafe.Add(ref meansReference, blockIdx) = 0;
                var block = pixels2D.Slice(row, col, BlockSize, BlockSize);
                for (var i = 0; i < BlockSize; i++)
                {
                    Unsafe.Add(ref meansReference, blockIdx) +=
                        TensorPrimitives.Sum(block.GetRowSpan(i)) / (double)(BlockSize * BlockSize);
                }

                blockIdx++;
            }
        }
    }
}