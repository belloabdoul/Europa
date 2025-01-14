using System.Diagnostics.CodeAnalysis;
using Core.Entities.Files;
using Core.Entities.Images;
using Core.Interfaces.Commons;
using Core.Interfaces.SimilarImages;
using Sdcb.LibRaw;

namespace Api.Implementations.SimilarImages.ImageProcessors;

public class LibRawImageProcessor(IServiceProvider serviceProvider) : IFileTypeIdentifier, IThumbnailGenerator
{
    public FileType GetFileType(string path)
    {
        try
        {
            using var context = RawContext.OpenFile(path);
            return FileType.LibRawImage;
        }
        catch (LibRawException)
        {
            return FileType.CorruptUnknownOrUnsupported;
        }
    }

    [SuppressMessage("ReSharper", "SwitchStatementHandlesSomeKnownEnumValuesWithDefault")]
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

        using var context = RawContext.OpenFile(imagePath);
        try
        {
            using var image = context.ExportThumbnail();

            var thumbnailGenerator = serviceProvider.GetRequiredKeyedService<IMainThumbnailGenerator>(image.ImageType);
            ArgumentNullException.ThrowIfNull(thumbnailGenerator);
            return thumbnailGenerator.GenerateThumbnail(image, width, height, pixels, colorSpace);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }
}