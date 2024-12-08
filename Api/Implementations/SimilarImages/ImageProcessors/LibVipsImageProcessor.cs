using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance;
using Core.Entities.Files;
using Core.Entities.Images;
using Core.Interfaces.Commons;
using Core.Interfaces.SimilarImages;
using NetVips;
using Sdcb.LibRaw;

namespace Api.Implementations.SimilarImages.ImageProcessors;

public class LibVipsImageProcessor : IFileTypeIdentifier, IThumbnailGenerator, IMainThumbnailGenerator
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
        switch (colorSpace)
        {
            case ColorSpace.Grayscale when pixels.Length < width * height:
                throw new ArgumentException($"Not enough space for thumbnail. Required buffer size is {width * height}",
                    nameof(pixels));
            case ColorSpace.Rgb when pixels.Length < width * height * 3:
                throw new ArgumentException(
                    $"Not enough space for thumbnail. Required buffer size is {width * height * 3}", nameof(pixels));
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
            using var colourSpaceImage = thumbnail.Colourspace(colorSpace switch
            {
                ColorSpace.Grayscale => Enums.Interpretation.Bw,
                _ => Enums.Interpretation.Srgb
            });
            using var imageWithoutAlpha = colourSpaceImage.Flatten();


            using var imageWithoutAlphaAsDouble = imageWithoutAlpha.Cast(Enums.BandFormat.Float);
            pointer = imageWithoutAlphaAsDouble.WriteToMemory(out var size);


            done = CopyPixels(pointer, pixels, Convert.ToInt32(size), colorSpace);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
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

        // For Grayscale or LAB there is nothing to do
        if (colorSpace is ColorSpace.Grayscale)
        {
            MemoryMarshal.Cast<byte, float>(providedPixels).CopyTo(pixels);
            return true;
        }

        var pixelsRgb = MemoryMarshal.Cast<byte, float>(providedPixels);

        // Separate channels - Ex: RGB-RGB-RGB to RRR-GGG-BBB
        var pixelPerChannel = pixelsRgb.Length / 3;
        var pixelsRgb2D = pixelsRgb.AsSpan2D(pixelPerChannel, 3);
        pixelsRgb2D.GetColumn(0).CopyTo(pixels[..pixelPerChannel]);
        pixelsRgb2D.GetColumn(1).CopyTo(pixels.Slice(pixelPerChannel, pixelPerChannel));
        pixelsRgb2D.GetColumn(2).CopyTo(pixels.Slice(2 * pixelPerChannel, pixelPerChannel));
        return true;
    }
}