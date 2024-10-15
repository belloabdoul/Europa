using System.Buffers;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance;
using Core.Entities;
using Core.Interfaces;
using DotNext.Buffers;

namespace Api.Implementations.SimilarImages.ImageHashGenerators;

// Block mean hash
public class BlockMeanHash : IImageHash
{
    private const int ImageSize = 256;
    private const int BlockSize = 16;
    private const int BlockPerRowOrCol = ImageSize / BlockSize;
    private const int LastRowOrColSize = ImageSize - BlockSize;
    private const int NbValuesPerBlock = BlockSize * BlockSize;
    private const short Zero = -1;
    private const short One = 1;
    private readonly bool _mode1;
    public int HashSize => _mode1 ? 968 : 256;

    public BlockMeanHash()
    {
        _mode1 = false;
    }

    public BlockMeanHash(bool mode1)
    {
        _mode1 = mode1;
    }

    public PerceptualHashAlgorithm PerceptualHashAlgorithm => PerceptualHashAlgorithm.BlockMeanHash;

    [SkipLocalsInit]
    public async ValueTask<float[]> GenerateHash(string imagePath, IThumbnailGenerator thumbnailGenerator)
    {
        using var pixels = new MemoryOwner<int>(ArrayPool<int>.Shared, ImageSize * ImageSize);
        await thumbnailGenerator.GenerateThumbnail(imagePath, ImageSize, ImageSize, pixels.Span);

        var pixRowOrColStep = BlockSize;
        int numOfBlocks;
        if (_mode1)
        {
            pixRowOrColStep /= 2;
            numOfBlocks = (BlockPerRowOrCol * 2 - 1) * (BlockPerRowOrCol * 2 - 1);
        }
        else
            numOfBlocks = BlockPerRowOrCol * BlockPerRowOrCol;

        Span<double> mean = stackalloc double[numOfBlocks];
        FindMean(pixels.Span, mean, pixRowOrColStep);
        return CreateHash(pixels.Span, mean);
    }

    [SkipLocalsInit]
    private float[] CreateHash(ReadOnlySpan<int> pixels, Span<double> means)
    {
        var median = TensorPrimitives.Sum(pixels) / (double)(ImageSize * ImageSize);

        var hash = new float[means.Length];
        ref var hashReference = ref MemoryMarshal.GetReference(hash.AsSpan());
        ref var meansReference = ref MemoryMarshal.GetReference(means);
        for (nint i = 0; i < means.Length; i++)
        {
            Unsafe.Add(ref hashReference, i) = Unsafe.Add(ref meansReference, i) < median ? Zero : One;
        }

        return hash;
    }

    [SkipLocalsInit]
    private void FindMean(ReadOnlySpan<int> pixels, Span<double> means, int pixRowOrColStep)
    {
        nint blockIdx = 0;
        Span<int> block = stackalloc int[NbValuesPerBlock];
        var pixels2D = pixels.AsSpan2D(ImageSize, ImageSize);
        ref var meansReference = ref MemoryMarshal.GetReference(means);

        for (var row = 0; row <= LastRowOrColSize; row += pixRowOrColStep)
        {
            for (var col = 0; col <= LastRowOrColSize; col += pixRowOrColStep)
            {
                pixels2D.Slice(col, row, BlockSize, BlockSize).CopyTo(block);
                Unsafe.Add(ref meansReference, blockIdx++) =
                    TensorPrimitives.Sum<int>(block) / (double)NbValuesPerBlock;
            }
        }
    }
}