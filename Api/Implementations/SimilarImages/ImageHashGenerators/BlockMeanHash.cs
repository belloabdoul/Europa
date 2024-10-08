using System.Numerics;
using System.Numerics.Tensors;
using CommunityToolkit.HighPerformance;
using Core.Entities;
using Core.Interfaces;

namespace Api.Implementations.SimilarImages.ImageHashGenerators;

// Block mean hash mode 1
public class BlockMeanHash : IImageHash
{
    private const int ImageSize = 256;
    private const int BlockSize = 32;
    private const int BlockPerRowOrCol = ImageSize / BlockSize;
    private const int LastRowOrColSize = ImageSize - BlockSize;
    private readonly bool _mode1;
    private static readonly Vector<double> VectorOfZeroes = Vector.Create<double>(0);
    private static readonly Vector<double> VectorOfOnes = Vector.Create<double>(1);

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

    public ValueTask<Half[]> GenerateHash(ReadOnlySpan<byte> pixels)
    {
        var pixColStep = BlockSize;
        var pixRowStep = BlockSize;
        int numOfBlocks;
        if (_mode1)
        {
            pixColStep /= 2;
            pixRowStep /= 2;
            numOfBlocks = (BlockPerRowOrCol * 2 - 1) * (BlockPerRowOrCol * 2 - 1);
        }
        else
            numOfBlocks = BlockPerRowOrCol * BlockPerRowOrCol;

        var mean = new double[numOfBlocks];
        FindMean(pixels, mean, pixRowStep, pixColStep);
        return ValueTask.FromResult(CreateHash(pixels, mean));
    }

    private Half[] CreateHash(ReadOnlySpan<byte> pixels, Span<double> means)
    {
        Span<double> meansCopy = stackalloc double[means.Length];
        means.CopyTo(meansCopy);
        // TensorPrimitives.ConvertChecked<byte, double>(means, meanssCopy);

        var median = Median(meansCopy, means.Length / 2);
        
        var hash = new Half[means.Length];
        for (var i = 0; i < means.Length; i++)
        {
            if (means[i] > median)
                hash[i] = (Half)1;
        }

        return hash;
    }

    private static void FindMean(ReadOnlySpan<byte> pixels, double[] mean, int pixRowStep, int pixColStep)
    {
        var blockIdx = 0;
        Span<byte> blockBytes = stackalloc byte[pixRowStep * pixColStep];
        Span<double> blockDoubles = stackalloc double[pixRowStep * pixColStep];
        var pixels2D = pixels.AsSpan2D(ImageSize, ImageSize);
        for (var row = 0; row <= LastRowOrColSize; row += pixRowStep)
        {
            for (var col = 0; col <= LastRowOrColSize; col += pixColStep)
            {
                pixels2D.Slice(col, row, BlockSize, BlockSize).CopyTo(blockBytes);
                TensorPrimitives.ConvertChecked<byte, double>(blockBytes, blockDoubles);
                mean[blockIdx++] = Median(blockDoubles, BlockSize * BlockSize / 2);
            }
        }
    }

    private static double Mean(Span<double> pixels)
    {
        return TensorPrimitives.Sum<double>(pixels) / pixels.Length;
    }

    private static double Median(Span<double> values, int halfway)
    {
        values.Sort();
        return TensorPrimitives.Sum<double>(values.Slice(halfway, 2)) / 2;
    }
}