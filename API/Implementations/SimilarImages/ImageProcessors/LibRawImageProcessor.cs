using System.Diagnostics.CodeAnalysis;
using Core.Entities;
using Core.Interfaces;
using Core.Interfaces.Common;
using Sdcb.LibRaw;

namespace API.Implementations.SimilarImages.ImageProcessors;

public class LibRawImageProcessor : IFileTypeIdentifier, IThumbnailGenerator
{
    private readonly IMainThumbnailGenerator _magicScalerThumbnailGenerator;
    private readonly IMainThumbnailGenerator _libVipsThumbnailGenerator;

    public FileSearchType GetAssociatedSearchType() => FileSearchType.Images;

    [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
    public LibRawImageProcessor(IEnumerable<IMainThumbnailGenerator> thumbnailGenerators)
    {
        _magicScalerThumbnailGenerator = thumbnailGenerators.First();
        _libVipsThumbnailGenerator = thumbnailGenerators.Last();
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
            using var image = context.ExportThumbnail(thumbnailIndex: 0);

            if (image.ImageType == ProcessedImageType.Jpeg)
                return _magicScalerThumbnailGenerator.GenerateThumbnail(image, width, height);

            return _libVipsThumbnailGenerator.GenerateThumbnail(image, width, height);
        }
        catch (Exception e)
        {
            return [];
        }
    }
}