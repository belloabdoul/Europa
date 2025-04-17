using System.Numerics;
using System.Runtime.CompilerServices;
using Core.Entities.Files;
using Core.Entities.Images;
using Core.Interfaces.Commons;
using Core.Interfaces.SimilarImages;
using DotNext;
using Sdcb.LibRaw;

namespace Api.Implementations.SimilarImages.ImageProcessors;

public sealed class LibRawImageProcessor(IServiceProvider serviceProvider) : IFileTypeIdentifier, IThumbnailGenerator
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

    public Result<bool> GenerateThumbnail<T>(string imagePath, int width, int height, ColorSpace colorSpace,
        bool inPolarCoordinates, Span<T> thumbnail) where T : struct, IBinaryFloatingPointIeee754<T>, IConvertible
    {
        var expected = width * height * Unsafe.As<ColorSpace, int>(ref colorSpace);

        if (expected == 0)
            return new Result<bool>(new ArgumentOutOfRangeException(nameof(colorSpace), "Unexpected color space"));
        if (thumbnail.Length < expected)
            return new Result<bool>(new ArgumentOutOfRangeException(nameof(thumbnail),
                $"Required thumbnail size {expected}, got {thumbnail.Length}"));

        using var context = RawContext.OpenFile(imagePath);
        using var image = context.ExportThumbnail();

        return serviceProvider.GetRequiredKeyedService<IMainThumbnailGenerator>(image.ImageType).GenerateThumbnail(
            image, width, height, colorSpace, inPolarCoordinates, thumbnail);
    }
}