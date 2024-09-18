using System.Diagnostics;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using CommunityToolkit.HighPerformance;
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

        // First convert row of bytes into row of float (this use SIMD) then generate the DCT for each row.
        for (var y = 0; y < Size; y++)
        {
            TensorPrimitives.ConvertChecked(pixels.Slice(y * Size, Size), sequence);
            Dct1D_SIMD(sequence, rows, y);
        }

        Span<float> matrix = stackalloc float[Size * Size];
        var rows2D = rows.AsSpan2D(Size, Size);

        // Calculate the DCT for each column.
        for (var x = 0; x < 8; x++)
        {
            rows2D.GetColumn(x).CopyTo(sequence);

            Dct1D_SIMD(sequence, matrix, x, limit: 8);
        }

        // Only use the top 8x8 values.
        Span<float> top8X8 = stackalloc float[Size];
        matrix.AsSpan2D(0, 8, 8, 56).CopyTo(top8X8);

        // Get average by skipping outlier first pixel
        var average = CalculateAverageWithoutOutlierFirstPixel(top8X8);

        // Compute the hash by comparing value to average
        var hash = new Half[Size];
        for (var i = 0; i < Size; i++)
            hash[i] = top8X8[i] > average ? Half.One : Half.Zero;

        return hash;
    }

    private static float CalculateAverageWithoutOutlierFirstPixel(Span<float> values)
    {
        Debug.Assert(values.Length == 64, "This DCT method works with 64 doubles.");
        return values[1..].Average();
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