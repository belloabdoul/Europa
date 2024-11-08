using System.Diagnostics;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance;
using CommunityToolkit.HighPerformance.Buffers;

namespace Api.Implementations.SimilarImages.ImageHashGenerators;

public class CosineTransform
{
    private readonly float[][] _dctCoefficients;
    private const int Size = 128;

    public CosineTransform()
    {
        _dctCoefficients = new float[Size][];

        // Vector from 1 to Size
        using var rowVector = SpanOwner<float>.Allocate(Size);
        ref var rowVectorReference = ref rowVector.DangerousGetReference();
        for (nint i = 0; i < Size; i++)
        {
            Unsafe.Add(ref rowVectorReference, i) = i;
        }

        for (nint coefficient = 0; coefficient < Size; coefficient++)
        {
            // Resulting vector
            var dctCoefficients = new float[Size];

            // Compute A (2.0 * i + 1.0) into result
            TensorPrimitives.Multiply(rowVector.Span, 2.0f, dctCoefficients);
            TensorPrimitives.Add(dctCoefficients, 1.0f, dctCoefficients);

            // Compute A * coeff * Math.PI / (2.0 * Size) into result
            TensorPrimitives.Multiply(dctCoefficients, coefficient, dctCoefficients);
            TensorPrimitives.Multiply(dctCoefficients, float.Pi, dctCoefficients);
            TensorPrimitives.Divide(dctCoefficients, 2.0f, dctCoefficients);
            TensorPrimitives.Divide(dctCoefficients, Size, dctCoefficients);

            // Now apply cos to obtain Math.Cos((2.0 * i + 1.0) * coeff * Math.PI / (2.0 * Size))
            TensorPrimitives.Cos<float>(dctCoefficients, dctCoefficients);

            // Multiply by sqrt(alpha / Size), with alpha = 1 if coefficient = 0, and alpha = 2 if coefficient > 0
            TensorPrimitives.Multiply(dctCoefficients, coefficient > 0 ? float.Sqrt(2) / 8.0f : 1.0f / 8.0f,
                dctCoefficients);

            _dctCoefficients[coefficient] = dctCoefficients;
        }
    }

    /// <summary>
    /// Compute the first 8 DCT result of a row in place.
    /// </summary>
    /// <param name="input">The samples to be transformed.</param>
    public void Forward8(Span<float> input)
    {
        Debug.Assert(input.Length == Size, $"This DCT method works with {Size} input.");
        ref var inputReference = ref MemoryMarshal.GetReference(input);
        using var temp = SpanOwner<float>.Allocate(Size);
        input.CopyTo(temp.Span);
        Unsafe.Add(ref inputReference, 0) = TensorPrimitives.Dot<float>(temp.Span, _dctCoefficients[0]);
        Unsafe.Add(ref inputReference, 1) = TensorPrimitives.Dot<float>(temp.Span, _dctCoefficients[1]);
        Unsafe.Add(ref inputReference, 2) = TensorPrimitives.Dot<float>(temp.Span, _dctCoefficients[2]);
        Unsafe.Add(ref inputReference, 3) = TensorPrimitives.Dot<float>(temp.Span, _dctCoefficients[3]);
        Unsafe.Add(ref inputReference, 4) = TensorPrimitives.Dot<float>(temp.Span, _dctCoefficients[4]);
        Unsafe.Add(ref inputReference, 5) = TensorPrimitives.Dot<float>(temp.Span, _dctCoefficients[5]);
        Unsafe.Add(ref inputReference, 6) = TensorPrimitives.Dot<float>(temp.Span, _dctCoefficients[6]);
        Unsafe.Add(ref inputReference, 7) = TensorPrimitives.Dot<float>(temp.Span, _dctCoefficients[7]);
    }

    /// <summary>
    /// Compute the top left 8x8 DCT result of a 2d array in place.
    /// </summary>
    /// <param name="input">The samples to be transformed.</param>
    public void Forward8X8(Span2D<float> input)
    {
        Debug.Assert(input.Height == Size && input.Width == Size, $"The input must be {Size}x{Size}.");
        for (var i = 0; i < input.Height; i++)
        {
            Forward8(input.GetRowSpan(i));
        }
        
        using var temp = SpanOwner<float>.Allocate(Size);
        for (var i = 0; i < 8; i++)
        {
            input.GetColumn(i).CopyTo(temp.Span);
            Forward8(temp.Span);
            input.GetColumn(i).TryCopyFrom(temp.Span[..8]);
        }
    }
}