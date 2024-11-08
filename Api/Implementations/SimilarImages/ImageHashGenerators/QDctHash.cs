using System.Collections;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance;
using CommunityToolkit.HighPerformance.Buffers;
using Core.Entities.Images;
using Core.Entities.SearchParameters;
using Core.Interfaces.SimilarImages;

namespace Api.Implementations.SimilarImages.ImageHashGenerators;

public class QDctHash : IImageHash
{
    private const int Size = 128;
    private const int FeatureSize = Size / 2;
    public int Width => Size;
    public int Height => Size;
    public int ImageSize => Size * Size;

    public int HashSize => FeatureSize * 4;
    public ColorSpace ColorSpace => ColorSpace.Rgb;
    public PerceptualHashAlgorithm PerceptualDctHashAlgorithm => PerceptualHashAlgorithm.QDctHash;
    private readonly CosineTransform _dct;

    public QDctHash(CosineTransform dct)
    {
        _dct = dct;
    }

    public Half[] GenerateHash(Span<float> pixels)
    {
        if (pixels.Length != ImageSize * 3)
            throw new ArgumentException(
                $"The pixel array is not of the size {ImageSize} required for perceptual hashing.");

        // Here we will receive 3 times as many pixels as the image, 1 for each channel compute each DCT
        using var top8X8R = SpanOwner<float>.Allocate(FeatureSize);
        using var top8X8G = SpanOwner<float>.Allocate(FeatureSize);
        using var top8X8B = SpanOwner<float>.Allocate(FeatureSize);

        // Dct for red channel
        ApplyDctForChannel(pixels[..ImageSize], top8X8R.Span);

        // Dct for green channel
        ApplyDctForChannel(pixels.Slice(ImageSize, ImageSize), top8X8G.Span);

        // Dct for blue channel
        ApplyDctForChannel(pixels.Slice(2 * ImageSize, ImageSize), top8X8B.Span);

        var hash = new Half[HashSize];
        using var finalDct = SpanOwner<float>.Allocate(HashSize);
        var (xi, eta, gamma) = (0.4472f, 0.8780f, 0.1705f);

        // C0
        TensorPrimitives.Multiply(top8X8R.Span, -xi, finalDct.Span[..FeatureSize]);
        TensorPrimitives.FusedMultiplyAdd(top8X8G.Span, -eta, finalDct.Span[..FeatureSize],
            finalDct.Span[..FeatureSize]);
        TensorPrimitives.FusedMultiplyAdd(top8X8B.Span, -gamma, finalDct.Span[..FeatureSize],
            finalDct.Span[..FeatureSize]);

        // C1
        TensorPrimitives.Multiply(top8X8B.Span, eta, finalDct.Span.Slice(FeatureSize, FeatureSize));
        TensorPrimitives.FusedMultiplyAdd(top8X8G.Span, -gamma, finalDct.Span.Slice(FeatureSize, FeatureSize),
            finalDct.Span.Slice(FeatureSize, FeatureSize));

        // C2
        TensorPrimitives.Multiply(top8X8B.Span, -xi, finalDct.Span.Slice(2 * FeatureSize, FeatureSize));
        TensorPrimitives.FusedMultiplyAdd(top8X8R.Span, gamma, finalDct.Span.Slice(2 * FeatureSize, FeatureSize),
            finalDct.Span.Slice(2 * FeatureSize, FeatureSize));

        // C3
        TensorPrimitives.Multiply(top8X8G.Span, xi, finalDct.Span.Slice(3 * FeatureSize, FeatureSize));
        TensorPrimitives.FusedMultiplyAdd(top8X8R.Span, -eta, finalDct.Span.Slice(3 * FeatureSize, FeatureSize),
            finalDct.Span.Slice(3 * FeatureSize, FeatureSize));

        TensorPrimitives.ConvertToHalf(finalDct.Span, hash);

        return hash;
    }

    private void ApplyDctForChannel(Span<float> channelPixels, Span<float> dctChannelResults)
    {
        var pixels2D = channelPixels.AsSpan2D(Height, Width);
        _dct.Forward8X8(pixels2D);
        pixels2D.Slice(0, 0, 8, 8).CopyTo(dctChannelResults);
        dctChannelResults[0] = 0;
    }
}