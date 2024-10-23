using System.Numerics.Tensors;
using Core.Entities;
using Core.Interfaces;
using Core.Interfaces.Common;
using NetVips;
using Sdcb.LibRaw;

namespace Api.Implementations.SimilarImages.ImageProcessors;

public class LibVipsImageProcessor : IFileTypeIdentifier, IThumbnailGenerator, IMainThumbnailGenerator
{
    public FileSearchType AssociatedSearchType => FileSearchType.Images;

    public FileType AssociatedImageType => FileType.LibVipsImage;

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

    public ValueTask<bool> GenerateThumbnail(ProcessedImage image, int width, int height, Span<byte> pixels)
    {
        using var imageFromBuffer = Image.NewFromMemory(image.DataPointer, Convert.ToUInt64(image.DataSize),
            image.Width, image.Height, image.Channels, Enums.BandFormat.Uchar);
        using var resizedImage = imageFromBuffer.ThumbnailImage(width, height, Enums.Size.Force);

        return ValueTask.FromResult(GenerateGrayscaleThumbnail(resizedImage, pixels));
    }

    public ValueTask<bool> GenerateThumbnail(string imagePath, int width, int height, Span<byte> pixels)
    {
        using var resizedImage = Image.Thumbnail(imagePath, width, height, Enums.Size.Force);

        return ValueTask.FromResult(GenerateGrayscaleThumbnail(resizedImage, pixels));
    }

    private static bool GenerateGrayscaleThumbnail(Image thumbnail, Span<byte> pixels)
    {
        var pointer = IntPtr.Zero;
        var done = false;
        try
        {
            using var grayscaleImage = thumbnail.Colourspace(Enums.Interpretation.Bw);
            using var imageWithoutAlpha = grayscaleImage.Flatten();
            pointer = imageWithoutAlpha.WriteToMemory(out var size);

            var length = Convert.ToInt32(size);

            ReadOnlySpan<byte> providedPixels;
            unsafe
            {
                providedPixels = new Span<byte>(pointer.ToPointer(), length);
            }

            TensorPrimitives.ConvertChecked(providedPixels, pixels);

            done = true;
        }
        catch (Exception)
        {
        }
        finally
        {
            if (pointer != IntPtr.Zero)
                NetVips.NetVips.Free(pointer);
        }

        return done;
    }
}