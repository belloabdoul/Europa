using System.Numerics;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using CommunityToolkit.HighPerformance;
using Core.Entities.Files;
using Core.Entities.Images;
using Core.Interfaces.SimilarImages;
using KFR.Interfaces;

namespace Api.Implementations.SimilarImages.ImageHashGenerators;

public sealed class QDftHash : IImageHash, IDisposable
{
    private const int Size = 256;

    private const int ChannelSize = Size * Size;

    private const int OutputChannelSize = Size * (Size / 2 + 1) * 2;
    private const ColorSpace ColourSpace = ColorSpace.Rgb;
    private const double Alpha = 0;
    private static readonly double Beta = -2 / Math.Sqrt(68);
    private static readonly double Gamma = 8 / Math.Sqrt(68);
    private const int P = 15;
    private const int HashSize = P * P - 1;

    private readonly IFft<float> _2dFloatFft;
    private readonly int _tempSize;

    public int Height => Size;
    public int Width => Size;
    public ColorSpace ColorSpace => ColourSpace;

    private readonly IServiceProvider _serviceProvider;

    public QDftHash(IFftFactory fftFactory, IServiceProvider serviceProvider)
    {
        _2dFloatFft = fftFactory.CreateFftInstance<float>(Size, Size);
        _tempSize = _2dFloatFft.GetTempBufferSize();
        _serviceProvider = serviceProvider;
    }

    [SkipLocalsInit]
    public unsafe BitArray GenerateHash(string imagePath, FileType fileType)
    {
        Span<float> rentedThumbnail = stackalloc float[ChannelSize * 3];

        var resizeResult = _serviceProvider.GetRequiredKeyedService<IThumbnailGenerator>(fileType)
            .GenerateThumbnail(imagePath, Width, Height, ColourSpace, true, rentedThumbnail);

        if (!resizeResult.IsSuccessful || !resizeResult.Value)
        {
            return new BitArray(0, []);
        }

        var redIn = rentedThumbnail[..ChannelSize];
        var greenIn = rentedThumbnail.Slice(ChannelSize, ChannelSize);
        var blueIn = rentedThumbnail.Slice(2 * ChannelSize, ChannelSize);

        Span<float> dftResults = stackalloc float[OutputChannelSize * 3];
        var redOut = dftResults[..OutputChannelSize];
        var greenOut = dftResults.Slice(OutputChannelSize, OutputChannelSize);
        var blueOut = dftResults.Slice(2 * OutputChannelSize, OutputChannelSize);

        Span<byte> temp = stackalloc byte[_tempSize];
        Apply2dDft(redIn, redOut, temp);
        TensorPrimitives.Divide(redOut, Size, redOut);

        Apply2dDft(greenIn, greenOut, temp);
        TensorPrimitives.Divide(greenOut, Size, greenOut);

        Apply2dDft(blueIn, blueOut, temp);
        TensorPrimitives.Divide(blueOut, Size, blueOut);

        Span<double> frequencies = stackalloc double[P * P];

        const int halfRange = P / 2;

        // Since KFR does not support reordering, the middle frequencies are not centered
        // But the middle start at the 0,0
        var (middleX, middleY) = (0, 0);
        var blockId = 0;
        var red2d = MemoryMarshal.Cast<float, Vector2>(redOut).AsSpan2D(Height, Width / 2 + 1);
        var green2d = MemoryMarshal.Cast<float, Vector2>(greenOut).AsSpan2D(Height, Width / 2 + 1);
        var blue2d = MemoryMarshal.Cast<float, Vector2>(blueOut).AsSpan2D(Height, Width / 2 + 1);

        for (var y = middleY - halfRange; y <= middleY + halfRange; y++)
        {
            for (var x = middleX - halfRange; x <= middleX + halfRange; x++)
            {
                frequencies[blockId++] = y switch
                {
                    < 0 when x <= 0 => GetQuaternionNorm(red2d[int.Abs(y), int.Abs(x)],
                        green2d[int.Abs(y), int.Abs(x)], blue2d[int.Abs(y), int.Abs(x)]),
                    < 0 => GetQuaternionNorm(red2d[Height + y, x], green2d[Height + y, x], blue2d[Height + y, x]),
                    >= 0 when x <= 0 => GetQuaternionNorm(red2d[Height - 1 - y, int.Abs(x)],
                        green2d[Height - 1 - y, int.Abs(x)], blue2d[Height - 1 - y, int.Abs(x)]),
                    >= 0 => GetQuaternionNorm(red2d[y, x], green2d[y, x], blue2d[y, x]),
                };
            }
        }

        var valuesPerVector = Vector512<double>.Count;
        var usedBytes = HashSize / valuesPerVector;
        var result = GC.AllocateUninitializedArray<byte>(usedBytes);

        for (var i = 0; i < usedBytes; i += 4)
        {
            (result[i], result[i + 1], result[i + 2], result[i + 3]) = (
                CompareSimd(frequencies.Slice(i * valuesPerVector, valuesPerVector),
                    frequencies.Slice(i * valuesPerVector + 1, valuesPerVector)),
                CompareSimd(frequencies.Slice((i + 1) * valuesPerVector, valuesPerVector),
                    frequencies.Slice((i + 1) * valuesPerVector + 1, valuesPerVector)),
                CompareSimd(frequencies.Slice((i + 2) * valuesPerVector, valuesPerVector),
                    frequencies.Slice((i + 2) * valuesPerVector + 1, valuesPerVector)),
                CompareSimd(frequencies.Slice((i + 3) * valuesPerVector, valuesPerVector),
                    frequencies.Slice((i + 3) * valuesPerVector + 1, valuesPerVector)));
        }

        return new BitArray(HashSize, result);
    }

    private static byte CompareSimd(Span<double> left, Span<double> right)
    {
        return Convert.ToByte(Vector512.GreaterThanOrEqual(
            Unsafe.As<double, Vector512<double>>(ref MemoryMarshal.GetReference(left)),
            Unsafe.As<double, Vector512<double>>(ref MemoryMarshal.GetReference(right))).ExtractMostSignificantBits());
    }

    private void Apply2dDft(Span<float> channel, Span<float> channelResult, Span<byte> temp)
    {
        _2dFloatFft.Forward(channel, channelResult, temp);
    }

    private static double GetQuaternionNorm(Vector2 red, Vector2 green, Vector2 blue)
    {
        // Calculate a, b, c, d
        var a = -Alpha * red.Y - Beta * green.Y - Gamma * blue.Y;
        var b = red.X + Gamma * green.Y - Beta * blue.Y;
        var c = green.X + Alpha * blue.Y - Gamma * red.Y;
        var d = blue.X + Beta * red.Y - Alpha * green.Y;
        
        ReadOnlySpan<double> result = [a, b, c, d];
        return Math.Sqrt(TensorPrimitives.SumOfSquares(result));
    }

    public void Dispose()
    {
        _2dFloatFft.Dispose();
    }
}