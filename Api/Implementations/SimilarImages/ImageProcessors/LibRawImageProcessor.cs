using System.Diagnostics.CodeAnalysis;
using Core.Entities;
using Core.Interfaces;
using Core.Interfaces.Common;
using Sdcb.LibRaw;

namespace Api.Implementations.SimilarImages.ImageProcessors;

public class LibRawImageProcessor : IFileTypeIdentifier, IThumbnailGenerator
{
    private readonly IMainThumbnailGenerator _libVipsThumbnailGenerator;
    private readonly IMainThumbnailGenerator _magicScalerThumbnailGenerator;

    [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
    public LibRawImageProcessor(IEnumerable<IMainThumbnailGenerator> thumbnailGenerators)
    {
        _magicScalerThumbnailGenerator = thumbnailGenerators.First();
        _libVipsThumbnailGenerator = thumbnailGenerators.Last();
    }

    public FileSearchType GetAssociatedSearchType()
    {
        return FileSearchType.Images;
    }

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

    public byte[] GenerateThumbnail(string imagePath, int width, int height)
    {
        using var context = RawContext.OpenFile(imagePath);
        try
        {
            using var image = context.ExportThumbnail();

            if (image.ImageType == ProcessedImageType.Jpeg)
                return _magicScalerThumbnailGenerator.GenerateThumbnail(image, width, height);

            return _libVipsThumbnailGenerator.GenerateThumbnail(image, width, height);
        }
        catch (Exception)
        {
            return [];
        }
    }
}