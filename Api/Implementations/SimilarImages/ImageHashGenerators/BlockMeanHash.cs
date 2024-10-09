using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance;
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
    private const int NbValuesPerBlock = BlockSize * BlockSize;
    private readonly bool _mode1;

    public BlockMeanHash()
    {
        _mode1 = false;
    }

    public BlockMeanHash(bool mode1)
    {
        _mode1 = mode1;
    }

    public int RequiredWidth => ImageSize;

    public int RequiredHeight => ImageSize;

    public PerceptualHashAlgorithm PerceptualHashAlgorithm => PerceptualHashAlgorithm.BlockMeanHash;

    [SkipLocalsInit]
    public ValueTask<Half[]> GenerateHash(ReadOnlySpan<byte> pixels)
    {
        Span<int> pixelsAsInts = stackalloc int[RequiredHeight * RequiredWidth];
        TensorPrimitives.ConvertChecked(pixels, pixelsAsInts);
        var pixRowOrColStep = BlockSize;
        int numOfBlocks;
        if (_mode1)
        {
            pixRowOrColStep /= 2;
            numOfBlocks = (BlockPerRowOrCol * 2 - 1) * (BlockPerRowOrCol * 2 - 1);
        }
        else
            numOfBlocks = BlockPerRowOrCol * BlockPerRowOrCol;

        var mean = new double[numOfBlocks];
        FindMean(pixelsAsInts, mean, pixRowOrColStep);
        return ValueTask.FromResult(CreateHash(pixelsAsInts, mean));
    }

    [SkipLocalsInit]
    private Half[] CreateHash(ReadOnlySpan<int> pixels, Span<double> means)
    {
        var median = TensorPrimitives.Sum(pixels) / (double)(RequiredHeight * RequiredWidth);

        var hash = new Half[means.Length];
        ref var hashReference = ref MemoryMarshal.GetReference(hash.AsSpan());
        ref var meansReference = ref MemoryMarshal.GetReference(means);
        for (nint i = 0; i < means.Length; i++)
        {
            Unsafe.Add(ref hashReference, i) = Unsafe.Add(ref meansReference, i) < median ? Half.Zero : Half.One;
        }

        return hash;
    }

    [SkipLocalsInit]
    private void FindMean(ReadOnlySpan<int> pixels, double[] means, int pixRowOrColStep)
    {
        nint blockIdx = 0;
        Span<int> block = stackalloc int[NbValuesPerBlock];
        var pixels2D = pixels.AsSpan2D(RequiredHeight, RequiredWidth);
        ref var meansReference = ref MemoryMarshal.GetReference(means.AsSpan());
        
        for (var row = 0; row <= LastRowOrColSize; row += pixRowOrColStep)
        {
            for (var col = 0; col <= LastRowOrColSize; col += pixRowOrColStep)
            {
                pixels2D.Slice(col, row, BlockSize, BlockSize).CopyTo(block);
                Unsafe.Add(ref meansReference, blockIdx++) = TensorPrimitives.Sum<int>(block) / (double)NbValuesPerBlock;
            }
        }
    }
}