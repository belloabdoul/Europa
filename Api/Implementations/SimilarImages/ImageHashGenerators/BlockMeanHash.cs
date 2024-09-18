using System.Numerics;
using System.Numerics.Tensors;
using CommunityToolkit.HighPerformance;
using Core.Interfaces;
using SimdLinq;

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

    public static int GetRequiredWidth() => ImageSize;

    public static int GetRequiredHeight() => ImageSize;
    
    public Half[] GenerateHash(ReadOnlySpan<byte> pixels)
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
        var hash = new Half[numOfBlocks];
        CreateHash(pixels, mean);
        TensorPrimitives.ConvertChecked<double, Half>(mean, hash);
        return hash;
    }

    private static void CreateHash(ReadOnlySpan<byte> pixels, Span<double> means)
    {
        Span<double> meansCopy = stackalloc double[means.Length];
        means.CopyTo(meansCopy);
        // TensorPrimitives.ConvertChecked<byte, double>(means, meanssCopy);
        
        var median = Median(meansCopy, means.Length / 2);
        var vectorOfMedian = Vector.Create(median);
        var numberOfValuesPerVector = Vector<double>.Count;
        var numberOfLoops = means.Length / numberOfValuesPerVector;

        for (var y = 0; y < numberOfLoops; y++)
        {
            var valuesToCompareToMedian =
                Vector.Create<double>(means.Slice(y * numberOfValuesPerVector, numberOfValuesPerVector));
            var condition = Vector.GreaterThan(valuesToCompareToMedian, vectorOfMedian);
            Vector.ConditionalSelect(condition, VectorOfOnes, VectorOfZeroes)
                .CopyTo(means.Slice(y * numberOfValuesPerVector, numberOfValuesPerVector));
        }

        if (means.Length % 8 == 1)
        {
            means[^1] = means[^1] > median ? 1 : 0;
        }
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
        return pixels.Average();
    }
    
    private static double Median(Span<double> values, int halfway)
    {
        values.Sort();
        return values.Slice(halfway, 2).Sum() / 2;
    }
}