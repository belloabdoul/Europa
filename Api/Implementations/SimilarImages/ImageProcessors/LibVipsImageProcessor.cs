using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance;
using Core.Entities.Files;
using Core.Entities.Images;
using Core.Interfaces.Commons;
using Core.Interfaces.SimilarImages;
using NetVips;
using Sdcb.LibRaw;

namespace Api.Implementations.SimilarImages.ImageProcessors;

public class LibVipsImageProcessor : IFileTypeIdentifier, IThumbnailGenerator, IMainThumbnailGenerator,
    IColorSpaceConverter
{
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

    public bool GenerateThumbnail(string imagePath, int width, int height, Span<float> pixels, ColorSpace colorSpace)
    {
        var pixelCount = width * height;
        switch (colorSpace)
        {
            case ColorSpace.Grayscale when pixels.Length < pixelCount:
                throw new ArgumentException($"Not enough space for thumbnail. Required buffer size is {pixelCount}",
                    nameof(pixels));
            case ColorSpace.Lab or ColorSpace.Rgb when pixels.Length < pixelCount * 3:
                throw new ArgumentException(
                    $"Not enough space for thumbnail. Required buffer size is {pixelCount * 12}", nameof(pixels));
        }

        using var resizedImage = Image.Thumbnail(imagePath, width, height, Enums.Size.Force);

        return GenerateThumbnail(resizedImage, pixels, colorSpace);
    }

    public bool GenerateThumbnail(ProcessedImage image, int width, int height, Span<float> pixels,
        ColorSpace colorSpace)
    {
        using var imageFromBuffer = Image.NewFromMemory(image.DataPointer, Convert.ToUInt64(image.DataSize),
            image.Width, image.Height, image.Channels, Enums.BandFormat.Uchar);
        using var resizedImage = imageFromBuffer.ThumbnailImage(width, height, Enums.Size.Force);

        return GenerateThumbnail(resizedImage, pixels, colorSpace);
    }

    private static bool GenerateThumbnail(Image thumbnail, Span<float> pixels, ColorSpace colorSpace)
    {
        var pointer = IntPtr.Zero;
        bool done;
        try
        {
            using var colourSpaceImage =
                thumbnail.Colourspace(colorSpace is ColorSpace.Grayscale ? Enums.Interpretation.Bw :
                    colorSpace is ColorSpace.Lab ? Enums.Interpretation.Lab : Enums.Interpretation.Srgb);
            using var imageWithoutAlpha = colourSpaceImage.Flatten();
            using var imageWithoutAlphaAsFloat = colourSpaceImage.Cast(Enums.BandFormat.Float);

            pointer = imageWithoutAlpha.WriteToMemory(out var size);
            done = CopyPixels(pointer, pixels, Convert.ToInt32(size), colorSpace);
        }
        catch (Exception)
        {
            done = false;
        }
        finally
        {
            if (pointer != IntPtr.Zero)
                NetVips.NetVips.Free(pointer);
        }

        return done;
    }

    private static bool CopyPixels(IntPtr pointer, Span<float> pixels, int length, ColorSpace colorSpace)
    {
        ReadOnlySpan<byte> providedPixels;
        unsafe
        {
            providedPixels = new Span<byte>(pointer.ToPointer(), length);
        }

        if (colorSpace is ColorSpace.Grayscale or ColorSpace.Lab)
        {
            MemoryMarshal.Cast<byte, float>(providedPixels).CopyTo(pixels);
            return true;
        }

        var pixelsRgb = MemoryMarshal.Cast<byte, float>(providedPixels);
        
        var pixelsRgb2D = pixelsRgb.AsSpan2D(length / 12, 3);

        pixelsRgb2D.GetColumn(0).CopyTo(pixels[..pixelsRgb2D.Height]);
        pixelsRgb2D.GetColumn(1).CopyTo(pixels.Slice(pixelsRgb2D.Height, pixelsRgb2D.Height));
        pixelsRgb2D.GetColumn(2).CopyTo(pixels.Slice(2 * pixelsRgb2D.Height, pixelsRgb2D.Height));
        
        return true;
    }


    public bool GetPixelsInLabColorSpace(ReadOnlyMemory<byte> pixels, int width, int height, Span<float> pixelsLabs)
    {
        var pointer = IntPtr.Zero;
        bool done;
        try
        {
            using var image = Image.NewFromMemory(pixels, width, height, 3, Enums.BandFormat.Uchar);
            using var labImage = image.Colourspace(Enums.Interpretation.Lab);
            pointer = labImage.WriteToMemory(out var size);

            done = CopyPixels(pointer, pixelsLabs, Convert.ToInt32(size), ColorSpace.Lab);
        }
        catch (Exception)
        {
            done = false;
        }
        finally
        {
            if (pointer != IntPtr.Zero)
                NetVips.NetVips.Free(pointer);
        }

        return done;
    }
}