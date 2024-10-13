using System.Numerics;
using Core.Entities;
using Core.Interfaces;
using Core.Interfaces.Common;
using Sdcb.LibRaw;

namespace Api.Implementations.SimilarImages.ImageProcessors;

public class LibRawImageProcessor : IFileTypeIdentifier, IThumbnailGenerator
{
    private readonly IMainThumbnailGenerator _libVipsThumbnailGenerator;
    private readonly IMainThumbnailGenerator _magicScalerThumbnailGenerator;

    public LibRawImageProcessor(IEnumerable<IMainThumbnailGenerator> thumbnailGenerators)
    {
        _magicScalerThumbnailGenerator = thumbnailGenerators.First();
        _libVipsThumbnailGenerator = thumbnailGenerators.Last();
    }

    public FileSearchType AssociatedSearchType => FileSearchType.Images;

    public FileType AssociatedImageType => FileType.LibRawImage;

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

    public ValueTask<bool> GenerateThumbnail<T>(string imagePath, int width, int height, Span<T> pixels)
        where T : INumberBase<T>
    {
        using var context = RawContext.OpenFile(imagePath);
        try
        {
            using var image = context.ExportThumbnail();

            return image.ImageType == ProcessedImageType.Jpeg
                ? _magicScalerThumbnailGenerator.GenerateThumbnail(image, width, height, pixels)
                : _libVipsThumbnailGenerator.GenerateThumbnail(image, width, height, pixels);
        }
        catch (Exception)
        {
            return ValueTask.FromResult(false);
        }
    }
}