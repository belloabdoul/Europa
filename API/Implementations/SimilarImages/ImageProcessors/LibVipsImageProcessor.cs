using Core.Entities;
using Core.Interfaces;
using Core.Interfaces.Common;
using Microsoft.IO;
using NetVips;
using Sdcb.LibRaw;

namespace API.Implementations.SimilarImages.ImageProcessors;

public class LibVipsImageProcessor : IFileTypeIdentifier, IThumbnailGenerator, IMainThumbnailGenerator
{
    private static readonly RecyclableMemoryStreamManager RecyclableMemoryStreamManager = new();

    public FileSearchType GetAssociatedSearchType()
    {
        return FileSearchType.Images;
    }

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

    public byte[] GenerateThumbnail(ProcessedImage image, int width, int height)
    {
        using var imageFromBuffer = Image.NewFromMemory(image.DataPointer, Convert.ToUInt64(image.DataSize),
            image.Width, image.Height, image.Channels, Enums.BandFormat.Uchar);
        using var resizedImage = imageFromBuffer.ThumbnailImage(width, height, Enums.Size.Force);
        using var grayscaleImage = resizedImage.Colourspace(Enums.Interpretation.Bw);
        using var imageWithoutAlpha = grayscaleImage.Flatten();
        return imageWithoutAlpha.WriteToMemory();
    }

    public byte[] GenerateThumbnail(string imagePath, int width, int height)
    {
        try
        {
            using var resizedImage =
                Image.Thumbnail(imagePath, width, height, Enums.Size.Force);
            using var grayscaleImage = resizedImage.Colourspace(Enums.Interpretation.Bw);
            using var imageWithoutAlpha = grayscaleImage.Flatten();
            return imageWithoutAlpha.WriteToMemory();
        }
        catch (Exception)
        {
            return [];
        }
    }
}