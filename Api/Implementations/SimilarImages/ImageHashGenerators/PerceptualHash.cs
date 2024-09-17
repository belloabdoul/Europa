using System.Diagnostics;
using System.Numerics;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using Core.Interfaces;
using SimdLinq;

namespace Api.Implementations.SimilarImages.ImageHashGenerators;

public class PerceptualHash : IImageHash
{
    private const int Size = 64;
    public static int GetRequiredWidth() => Size;
    public static int GetRequiredHeight() => Size;
    private static readonly float[][] DctCoeffsSimd = GenerateDctCoeffsSimd();

    [SkipLocalsInit]
    public Half[] GenerateHash(ReadOnlySpan<byte> pixels)
    {
        Span<float> rows = stackalloc float[Size * Size];

        Span<float> sequence = stackalloc float[Size];
        // Calculate the DCT for each row.
        for (var y = 0; y < Size; y++)
        {
            TensorPrimitives.ConvertChecked(pixels.Slice(y * Size, Size), sequence);
            Dct1D_SIMD(sequence, rows, y);
        }

        Span<float> matrix = stackalloc float[Size * Size];
        // Calculate the DCT for each column.
        for (var x = 0; x < 8; x++)
        {
            for (var y = 0; y < Size; y++)
            {
                sequence[y] = rows[y * Size + x];
            }

            Dct1D_SIMD(sequence, matrix, x, limit: 8);
        }

        // Only use the top 8x8 values.
        Span<float> top8X8 = stackalloc float[Size];
        Span<float> top8X8Median = stackalloc float[Size];
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                top8X8[y * 8 + x] = matrix[y * Size + x];
                top8X8Median[y * 8 + x] = top8X8[y * 8 + x];
            }
        }

        // Get Median.
        var median = CalculateMedian64Values(top8X8Median);

        // Calculate hash.
        // Calculate hash.
        var vectorOfMedian = Vector.Create(median);
        // The number of loop is how many times we will need to loop through the top 8x8 to compare each value to median
        // with Simd. Since vector has a value by default of 256 bit Vector<double> would fit 4 double per vector. So we
        // would have 16 comparisons
        var numberOfValuesPerVector = Vector<float>.Count;
        var numberOfLoops = Size / numberOfValuesPerVector;
        for (var y = 0; y < numberOfLoops; y++)
        {
            var valuesToCompareToMedian =
                Vector.Create<float>(top8X8.Slice(y * numberOfValuesPerVector, numberOfValuesPerVector));
            var condition = Vector.GreaterThan(valuesToCompareToMedian, vectorOfMedian);
            Vector.ConditionalSelect(condition, Vector<float>.One, Vector<float>.Zero)
                .CopyTo(top8X8.Slice(y * numberOfValuesPerVector, numberOfValuesPerVector));
        }

        // The final hash is an array of double. Convert to byte
        var hash = new Half[Size];
        TensorPrimitives.ConvertToHalf(top8X8, hash);

        return hash;
    }

    private static float CalculateMedian64Values(Span<float> values)
    {
        Debug.Assert(values.Length == 64, "This DCT method works with 64 doubles.");
        values.Sort();
        return values.Slice(32, 2).Sum() / 2;
    }

    [SkipLocalsInit]
    private static float[][] GenerateDctCoeffsSimd()
    {
        var results = new float[Size][];

        // Vector from 1 to Size
        var rowVector = Enumerable.Range(0, Size).Select(Convert.ToSingle).ToArray();

        for (var coeff = 0; coeff < Size; coeff++)
        {
            // Resulting vector
            var vectorResult = new float[Size];

            // Compute A (2.0 * i + 1.0) into result
            TensorPrimitives.Multiply(rowVector, 2, vectorResult.AsSpan());
            TensorPrimitives.Add(vectorResult, 1, vectorResult.AsSpan());

            // Compute A * coeff * Math.PI / (2.0 * Size) into result
            TensorPrimitives.Multiply(vectorResult, coeff, vectorResult.AsSpan());
            TensorPrimitives.Multiply(vectorResult, Convert.ToSingle(Math.PI), vectorResult.AsSpan());
            TensorPrimitives.Divide(vectorResult, 2, vectorResult.AsSpan());
            TensorPrimitives.Divide(vectorResult, Size, vectorResult.AsSpan());

            // Now apply cos to obtain Math.Cos((2.0 * i + 1.0) * coeff * Math.PI / (2.0 * Size))
            TensorPrimitives.Cos(vectorResult, vectorResult.AsSpan());

            results[coeff] = vectorResult;
        }

        return results;
    }

    private static void Dct1D_SIMD(ReadOnlySpan<float> valuesRaw, Span<float> coefficients, int ci, int limit = Size)
    {
        Debug.Assert(valuesRaw.Length == 64, "This DCT method works with 64 doubles.");

        for (var coeff = 0; coeff < limit; coeff++)
            coefficients[ci * Size + coeff] = TensorPrimitives.Dot(valuesRaw, DctCoeffsSimd[coeff]);
    }
}