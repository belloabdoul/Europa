using System.Runtime.InteropServices;
using Core.Entities;
using Core.Interfaces;
using Core.Interfaces.Common;
using DotNext;
using DotNext.Buffers;
using Microsoft.IO;
using NetVips;
using Pipelines.Sockets.Unofficial;
using Sdcb.LibRaw;
using File = Core.Entities.File;

namespace API.Implementations.SimilarImages.ImageProcessors;

public class LibVipsImageProcessor : IFileTypeIdentifier, IThumbnailGenerator, IMainThumbnailGenerator
{
    public FileSearchType GetAssociatedSearchType() => FileSearchType.Images;
    private static readonly RecyclableMemoryStreamManager RecyclableMemoryStreamManager = new();

    public FileType GetFileType(string path)
    {
        try
        {
            // Return if the image is a large image or not depending on if its uncompressed size is bigger than 10Mb
            using var image = Image.NewFromFile(path, memory: false, access: Enums.Access.Sequential,
                failOn: Enums.FailOn.Error);
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

    public byte[] GenerateThumbnail(string imagePath, int width, int height)
    {
        try
        {
            using var resizedImage =
                Image.Thumbnail(imagePath, width: width, height: height, size: Enums.Size.Force);
            using var grayscaleImage = resizedImage.Colourspace(Enums.Interpretation.Bw);
            using var imageWithoutAlpha = grayscaleImage.Flatten();
            return imageWithoutAlpha.WriteToMemory();
        }
        catch (Exception)
        {
            return [];
        }
    }

    public byte[] GenerateThumbnail(ProcessedImage image, int width, int height)
    {
        using var imageFromBuffer = Image.NewFromMemory(image.DataPointer, Convert.ToUInt64(image.DataSize), image.Width, image.Height, image.Channels, Enums.BandFormat.Uchar);
        using var resizedImage = imageFromBuffer.ThumbnailImage(width, height: height, size: Enums.Size.Force);
        using var grayscaleImage = resizedImage.Colourspace(Enums.Interpretation.Bw);
        using var imageWithoutAlpha = grayscaleImage.Flatten();
        return imageWithoutAlpha.WriteToMemory();
    }
}