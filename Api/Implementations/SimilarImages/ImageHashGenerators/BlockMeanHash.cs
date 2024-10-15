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
    private const byte Zero = 0;
    private const byte One = 1;
    private readonly int _pixelRowOrColStep;
    private readonly int _hashSize;

    public BlockMeanHash()
    {
        _hashSize = BlockPerRowOrCol * BlockPerRowOrCol;
        _pixelRowOrColStep = BlockSize;
    }

    public BlockMeanHash(bool mode1)
    {
        _pixelRowOrColStep = BlockSize;
        if (mode1)
        {
            _hashSize = (BlockPerRowOrCol * 2 - 1) * (BlockPerRowOrCol * 2 - 1);
            _pixelRowOrColStep /= 2;
        }
        else
            _hashSize = BlockPerRowOrCol * BlockPerRowOrCol;
    }

    public PerceptualHashAlgorithm PerceptualHashAlgorithm => PerceptualHashAlgorithm.BlockMeanHash;

    [SkipLocalsInit]
    public async ValueTask<byte[]> GenerateHash(string imagePath, IThumbnailGenerator thumbnailGenerator)
    {
        using var pixels = new MemoryOwner<int>(ArrayPool<int>.Shared, ImageSize * ImageSize);
        await thumbnailGenerator.GenerateThumbnail(imagePath, ImageSize, ImageSize, pixels.Span);
        
        Span<double> mean = stackalloc double[_hashSize];
        FindMean(pixels.Span, mean);
        return CreateHash(pixels.Span, mean);
    }

    [SkipLocalsInit]
    private byte[] CreateHash(ReadOnlySpan<int> pixels, Span<double> means)
    {
        var median = TensorPrimitives.Sum(pixels) / (double)(ImageSize * ImageSize);

        var hash = new byte[means.Length];
        ref var hashReference = ref MemoryMarshal.GetReference(hash.AsSpan());
        ref var meansReference = ref MemoryMarshal.GetReference(means);
        for (nint i = 0; i < means.Length; i++)
        {
            Unsafe.Add(ref hashReference, i) = Unsafe.Add(ref meansReference, i) < median ? Zero : One;
        }

        return hash;
    }

    [SkipLocalsInit]
    private void FindMean(ReadOnlySpan<int> pixels, Span<double> means)
    {
        nint blockIdx = 0;
        Span<int> block = stackalloc int[NbValuesPerBlock];
        var pixels2D = pixels.AsSpan2D(ImageSize, ImageSize);
        ref var meansReference = ref MemoryMarshal.GetReference(means);

        for (var row = 0; row <= LastRowOrColSize; row += _pixelRowOrColStep)
        {
            for (var col = 0; col <= LastRowOrColSize; col += _pixelRowOrColStep)
            {
                pixels2D.Slice(col, row, BlockSize, BlockSize).CopyTo(block);
                Unsafe.Add(ref meansReference, blockIdx++) =
                    TensorPrimitives.Sum<int>(block) / (double)NbValuesPerBlock;
            }
        }
    }
}