using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Core.Entities.Files;
using Core.Entities.Images;
using Core.Interfaces.Commons;
using Core.Interfaces.SimilarImages;
using DotNext;
using NetVips;
using Sdcb.LibRaw;

namespace Api.Implementations.SimilarImages.ImageProcessors;

public sealed class LibVipsImageProcessor : IFileTypeIdentifier, IThumbnailGenerator, IMainThumbnailGenerator,
    IPolarCoordinatesConverter, IDisposable
{
    private readonly ConcurrentDictionary<Vector2, Image> _polarCoordinates = new();

    private static Enums.BandFormat GetBandFormat<T>() =>
        Unsafe.SizeOf<T>() == 8 ? Enums.BandFormat.Double : Enums.BandFormat.Float;

    public FileType GetFileType(string path)
    {
        try
        {
            // Return if the image is a large image or not depending on if its uncompressed size is bigger than 10Mb
            using var image = Image.NewFromFile(path, false, Enums.Access.Sequential,
                Enums.FailOn.Error);
            var loader = (string)image.Get("vips-loader");
            if (loader.Contains("gif", StringComparison.InvariantCultureIgnoreCase) ||
                loader.Contains("webp", StringComparison.InvariantCultureIgnoreCase) ||
                loader.Contains("png", StringComparison.InvariantCultureIgnoreCase))
                return image.GetFields().Contains("n-pages", StringComparer.InvariantCultureIgnoreCase)
                    ? FileType.Animation
                    : FileType.LibVipsImage;
            return FileType.LibVipsImage;
        }
        catch (VipsException)
        {
            return FileType.CorruptUnknownOrUnsupported;
        }
    }

    public Result<bool> GenerateThumbnail<T>(string imagePath, int width, int height, ColorSpace colorSpace,
        bool inPolarCoordinates, Span<T> thumbnail) where T : struct, IBinaryFloatingPointIeee754<T>, IConvertible
    {
        var expected = width * height * Unsafe.As<ColorSpace, int>(ref colorSpace);

        if (expected == 0)
            return new Result<bool>(new ArgumentOutOfRangeException(nameof(colorSpace), "Unexpected color space"));
        if (thumbnail.Length < expected)
            return new Result<bool>(new ArgumentOutOfRangeException(nameof(thumbnail),
                $"Required thumbnail size {expected}, got {thumbnail.Length}"));

        using var resizedImage =
            Image.Thumbnail(imagePath, width, height, Enums.Size.Force, false, Enums.Interesting.All, true);

        return GenerateThumbnail(resizedImage, colorSpace, inPolarCoordinates, thumbnail);
    }

    public Result<bool> GenerateThumbnail<T>(ProcessedImage image, int width, int height, ColorSpace colorSpace,
        bool inPolarCoordinates, Span<T> thumbnail)
        where T : struct, IBinaryFloatingPointIeee754<T>, IConvertible
    {
        using var imageFromBuffer = Image.NewFromMemory(image.DataPointer, Convert.ToUInt64(image.DataSize),
            image.Width, image.Height, image.Channels, Enums.BandFormat.Uchar);
        using var resizedImage = imageFromBuffer.ThumbnailImage(width, height, Enums.Size.Force);

        return GenerateThumbnail(resizedImage, colorSpace, inPolarCoordinates, thumbnail);
    }

    private static Image GetCartesianCoordinates(int height, int width)
    {
        using var coordinates = Image.Xyz(width, height);
        var radiusScale = width / Math.Log(Math.Min(width, height) / 2.0);
        var angleScale = height / 360.0;

        using var distancesLog = coordinates[0] / radiusScale;
        using var distances = distancesLog.Exp();
        using var angles = coordinates[1] / angleScale;
        using var x = distances * angles.Cos();
        using var y = distances * angles.Sin();
        using var cartesianCoordinates = x.Bandjoin(y);
        return cartesianCoordinates + [width / 2.0, height / 2.0];
    }

    private Result<bool> GenerateThumbnail<T>(Image resizedImage, ColorSpace colorSpace, bool inPolarCoordinates,
        Span<T> thumbnail) where T : struct, IBinaryFloatingPointIeee754<T>, IConvertible
    {
        var pointer = IntPtr.Zero;
        try
        {
            using var colourSpaceImage = resizedImage.Colourspace(colorSpace switch
            {
                ColorSpace.Grayscale => Enums.Interpretation.Bw,
                _ => Enums.Interpretation.Srgb
            });

            using var blurredImage = colourSpaceImage.Gaussblur(2.5, 0.01, Enums.Precision.Float);
            using var imageAsType = blurredImage.Cast(GetBandFormat<T>());

            if (inPolarCoordinates)
            {
                var width = resizedImage.Width;
                var height = resizedImage.Height;
                using var cartesianImage = imageAsType.Mapim(_polarCoordinates.GetOrAdd(
                    Vector2.Create(width, height),
                    size => GetCartesianCoordinates(Convert.ToInt32(size.X), Convert.ToInt32(size.Y))));
                if (!cartesianImage.HasAlpha())
                {
                    pointer = cartesianImage.WriteToMemory(out var size);
                    return CopyPixels(pointer, colorSpace, thumbnail, Convert.ToInt32(size));
                }
                else
                {
                    using var imageWithoutAlpha = cartesianImage.Flatten();
                    pointer = imageWithoutAlpha.WriteToMemory(out var size);
                    return CopyPixels(pointer, colorSpace, thumbnail, Convert.ToInt32(size));
                }
            }
            else if (!imageAsType.HasAlpha())
            {
                pointer = imageAsType.WriteToMemory(out var size);
                return CopyPixels(pointer, colorSpace, thumbnail, Convert.ToInt32(size));
            }
            else
            {
                using var imageWithoutAlpha = imageAsType.Flatten();
                pointer = imageWithoutAlpha.WriteToMemory(out var size);
                return CopyPixels(pointer, colorSpace, thumbnail, Convert.ToInt32(size));
            }
        }
        catch (Exception e)
        {
            return new Result<bool>(e);
        }
        finally
        {
            if (pointer != IntPtr.Zero)
                NetVips.NetVips.Free(pointer);
        }
    }

    private static bool CopyPixels<T>(IntPtr pointer, ColorSpace colorSpace, Span<T> pixels, int length)
        where T : struct, IBinaryFloatingPointIeee754<T>, IConvertible
    {
        ReadOnlySpan<T> providedPixels;
        unsafe
        {
            providedPixels = new Span<T>(pointer.ToPointer(), length / Unsafe.SizeOf<T>());
        }

        // For Grayscale there is nothing to do
        if (colorSpace is ColorSpace.Grayscale)
        {
            providedPixels.CopyTo(pixels);
            return true;
        }

        // Separate channels - Ex: RGB-RGB-RGB to RRR-GGG-BBB
        var srcPixelsPerChannel = providedPixels.Length / 3;

        for (var i = 0; i < srcPixelsPerChannel; i++)
        {
            var redIndex = 3 * i;
            (pixels[i], pixels[srcPixelsPerChannel + i], pixels[2 * srcPixelsPerChannel + i]) =
                (providedPixels[redIndex], providedPixels[redIndex + 1], providedPixels[redIndex + 2]);
        }

        return true;
    }

    public unsafe bool ConvertToPolarCoordinates<T>(ReadOnlySpan<byte> input, Span<T> output, int width, int height,
        ColorSpace colorSpace) where T : struct, IBinaryFloatingPointIeee754<T>, IConvertible
    {
        using var importedImage =
            Image.NewFromMemory(new IntPtr(Unsafe.AsPointer(ref MemoryMarshal.GetReference(input))),
                Convert.ToUInt64(input.Length), width, height, Unsafe.As<ColorSpace, int>(ref colorSpace),
                Enums.BandFormat.Uchar);
        using var cartesianImage = importedImage.Mapim(_polarCoordinates.GetOrAdd(
            Vector2.Create(width, height),
            size => GetCartesianCoordinates(Convert.ToInt32(size.X), Convert.ToInt32(size.Y))));
        using var imageAsType = cartesianImage.Cast(GetBandFormat<T>());
        var pointer = IntPtr.Zero;
        try
        {
            pointer = imageAsType.WriteToMemory(out var size);
            return CopyPixels(pointer, colorSpace, output, Convert.ToInt32(size));
        }
        finally
        {
            if (pointer != IntPtr.Zero)
                NetVips.NetVips.Free(pointer);
        }
    }

    public void Dispose()
    {
        foreach (var polarCoordinates in _polarCoordinates)
        {
            polarCoordinates.Value.Dispose();
        }

        _polarCoordinates.Clear();
    }
}