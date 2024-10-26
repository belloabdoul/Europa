using System.Collections;
using System.Diagnostics;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance;
using CommunityToolkit.HighPerformance.Buffers;
using Core.Entities;
using Core.Entities.Images;
using Core.Entities.SearchParameters;
using Core.Interfaces;
using Core.Interfaces.SimilarImages;

namespace Api.Implementations.SimilarImages.ImageHashGenerators;

public class PerceptualHash : IImageHash
{
    private const int Size = 32;
    public int HashSize => Size + Size;
    public PerceptualHashAlgorithm PerceptualHashAlgorithm => PerceptualHashAlgorithm.PerceptualHash;

    private static readonly float[][] DctCoefficients;

    [SkipLocalsInit]
    static PerceptualHash()
    {
        DctCoefficients = new float[Size][];

        // Vector from 1 to Size
        Span<float> rowVector = stackalloc float[Size];
        ref var rowVectorReference = ref MemoryMarshal.GetReference(rowVector);
        for (nint i = 0; i < Size; i++)
        {
            Unsafe.Add(ref rowVectorReference, i) = i;
        }

        for (nint coefficient = 0; coefficient < Size; coefficient++)
        {
            // Resulting vector
            var dctCoefficients = new float[Size];

            // Compute A (2.0 * i + 1.0) into result
            TensorPrimitives.Multiply(rowVector, 2, dctCoefficients);
            TensorPrimitives.Add(dctCoefficients, 1, dctCoefficients);

            // Compute A * coeff * Math.PI / (2.0 * Size) into result
            TensorPrimitives.Multiply(dctCoefficients, coefficient, dctCoefficients);
            TensorPrimitives.Multiply(dctCoefficients, Single.Pi, dctCoefficients);
            TensorPrimitives.Divide(dctCoefficients, 2, dctCoefficients);
            TensorPrimitives.Divide(dctCoefficients, Size, dctCoefficients);

            // Now apply cos to obtain Math.Cos((2.0 * i + 1.0) * coeff * Math.PI / (2.0 * Size))
            TensorPrimitives.Cos<float>(dctCoefficients, dctCoefficients);

            // Multiply by sqrt(alpha / Size), with alpha = 1 if coefficient = 0, and alpha = 2 if coefficient > 0
            TensorPrimitives.Multiply(dctCoefficients, coefficient > 0 ? float.Sqrt(2) / 8f : 1f / 8f,
                dctCoefficients);

            DctCoefficients[coefficient] = dctCoefficients;
        }
    }

    [SkipLocalsInit]
    public async ValueTask<BitArray> GenerateHash(string imagePath, IThumbnailGenerator thumbnailGenerator)
    {
        using var pixels = MemoryOwner<byte>.Allocate(Size * Size);
        using var pixelsAsFloats = MemoryOwner<float>.Allocate(Size * Size);

        await thumbnailGenerator.GenerateThumbnail(imagePath, Size, Size, pixels.Span);
        TensorPrimitives.ConvertChecked<byte, float>(pixels.Span, pixelsAsFloats.Span);

        using var dctRowsResults = MemoryOwner<float>.Allocate(8 * Size);
        // The 2D DCT is given by R = C . X . t(C) where . is the matrix product and C the DCT matrix

        // DCT for rows : since the columns of t(C) are the rows of C, the dot product of the rows of X by the columns
        // of t(C) is the same as the dot product of the rows of X by the rows of C
        // We only do the 8 first columns of the DCT matrix since we only need the top left 8 x 8. The resulting matrix
        // is 32 rows x 8 columns
        ref var pixelsReference = ref pixelsAsFloats.DangerousGetReference();
        for (nint y = 0; y < Size; y++)
        {
            Dct1D_SIMD(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.Add(ref pixelsReference, y * Size), Size),
                dctRowsResults.Span, y);
        }

        // Reuse the previously used array rented with pixelsAsFloats for the column
        Span<float> column = stackalloc float[Size];
        using var top8X8 = pixelsAsFloats[..HashSize];
        
        // DCT for columns : we multiply each row of our DCT matrix by a column of our previous result to avoid multiple
        // copies. In this case, the result of this multiplication is aligned on the column since we lock according to 
        // the second member of the matrix product. With this, we automatically get the 8 x 8 top left frequencies
        // need for pHash
        var dctRowsResults2D = dctRowsResults.Span.AsSpan2D(Size, 8);
        for (nint x = 0; x < 8; x++)
        {
            dctRowsResults2D.GetColumn((int)x).CopyTo(column);
            Dct1D_SIMD(column, top8X8.Span, x, true);
        }

        // Ignore the top left pixel
        ref var top8X8Reference = ref top8X8.DangerousGetReference();
        top8X8Reference = 0;

        // Compute the average
        var mean = TensorPrimitives.Sum(top8X8.Span) / (HashSize);

        // Set hash to 1 or 0 depending on if the current pixel in the DCT is greater than the average
        var hash = new BitArray(HashSize);
        for (nint i = 0; i < HashSize; i++)
        {
            if (Unsafe.Add(ref top8X8Reference, i) <= mean)
                continue;
            hash[(int)i] = true;
        }

        return hash;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Dct1D_SIMD(ReadOnlySpan<float> values, Span<float> dctResults, nint index,
        bool isColumn = false)
    {
        Debug.Assert(values.Length == 32, "This DCT method works with 32 values.");
        ref var dctResultsReference = ref MemoryMarshal.GetReference(dctResults);
        if (!isColumn)
        {
            Unsafe.Add(ref dctResultsReference, index * 8) =
                TensorPrimitives.Dot(values, DctCoefficients[0]);
            Unsafe.Add(ref dctResultsReference, index * 8 + 1) =
                TensorPrimitives.Dot(values, DctCoefficients[1]);
            Unsafe.Add(ref dctResultsReference, index * 8 + 2) =
                TensorPrimitives.Dot(values, DctCoefficients[2]);
            Unsafe.Add(ref dctResultsReference, index * 8 + 3) =
                TensorPrimitives.Dot(values, DctCoefficients[3]);
            Unsafe.Add(ref dctResultsReference, index * 8 + 4) =
                TensorPrimitives.Dot(values, DctCoefficients[4]);
            Unsafe.Add(ref dctResultsReference, index * 8 + 5) =
                TensorPrimitives.Dot(values, DctCoefficients[5]);
            Unsafe.Add(ref dctResultsReference, index * 8 + 6) =
                TensorPrimitives.Dot(values, DctCoefficients[6]);
            Unsafe.Add(ref dctResultsReference, index * 8 + 7) =
                TensorPrimitives.Dot(values, DctCoefficients[7]);
        }
        else
        {
            Unsafe.Add(ref dctResultsReference, index) =
                TensorPrimitives.Dot(values, DctCoefficients[0]);
            Unsafe.Add(ref dctResultsReference, 8 + index) =
                TensorPrimitives.Dot(values, DctCoefficients[1]);
            Unsafe.Add(ref dctResultsReference, 16 + index) =
                TensorPrimitives.Dot(values, DctCoefficients[2]);
            Unsafe.Add(ref dctResultsReference, 24 + index) =
                TensorPrimitives.Dot(values, DctCoefficients[3]);
            Unsafe.Add(ref dctResultsReference, 32 + index) =
                TensorPrimitives.Dot(values, DctCoefficients[4]);
            Unsafe.Add(ref dctResultsReference, 40 + index) =
                TensorPrimitives.Dot(values, DctCoefficients[5]);
            Unsafe.Add(ref dctResultsReference, 48 + index) =
                TensorPrimitives.Dot(values, DctCoefficients[6]);
            Unsafe.Add(ref dctResultsReference, 56 + index) =
                TensorPrimitives.Dot(values, DctCoefficients[7]);
        }
    }
}