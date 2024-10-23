using System.Collections;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance;
using CommunityToolkit.HighPerformance.Buffers;
using Core.Entities;
using Core.Interfaces;

namespace Api.Implementations.SimilarImages.ImageHashGenerators;

// Block mean hash
public class BlockMeanHash : IImageHash
{
    private const int ImageSize = 256;
    private const int BlockSize = 16;
    private const int BlockPerRowOrCol = ImageSize / BlockSize;
    private const int LastRowOrColSize = ImageSize - BlockSize;
    private readonly int _pixelRowOrColStep;
    public int HashSize { get; }
    
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
    public async ValueTask<BitArray> GenerateHash(string imagePath, IThumbnailGenerator thumbnailGenerator)
    {
        using var pixels = MemoryOwner<byte>.Allocate(ImageSize * ImageSize);
        using var pixelsAsInts = MemoryOwner<int>.Allocate(ImageSize * ImageSize);

        await thumbnailGenerator.GenerateThumbnail(imagePath, ImageSize, ImageSize, pixels.Span);
        TensorPrimitives.ConvertChecked<byte, int>(pixels.Span, pixelsAsInts.Span);
        
        using var mean = MemoryOwner<double>.Allocate(HashSize);
        FindMean(pixelsAsInts.Span, mean.Span);
        return CreateHash(pixelsAsInts.Span, mean.Span);
    }

    [SkipLocalsInit]
    private BitArray CreateHash(ReadOnlySpan<int> pixels, Span<double> means)
    {
        var median = TensorPrimitives.Sum(pixels) / (double)(ImageSize * ImageSize);

        var hash = new BitArray(means.Length);
        ref var meansReference = ref MemoryMarshal.GetReference(means);
        for (nint i = 0; i < means.Length; i++)
        {
            if(Unsafe.Add(ref meansReference, i) < median)
                continue;
            hash[(int)i] = true;
        }

        return hash;
    }

    [SkipLocalsInit]
    private void FindMean(ReadOnlySpan<int> pixels, Span<double> means)
    {
        nint blockIdx = 0;
        var pixels2D = pixels.AsSpan2D(ImageSize, ImageSize);
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