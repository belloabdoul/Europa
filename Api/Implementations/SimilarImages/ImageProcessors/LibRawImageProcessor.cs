using Core.Entities.Files;
using Core.Interfaces.Commons;
using Core.Interfaces.SimilarImages;
using Sdcb.LibRaw;

namespace Api.Implementations.SimilarImages.ImageProcessors;

public class LibRawImageProcessor : IFileTypeIdentifier, IThumbnailGenerator
{
    private readonly IServiceProvider _serviceProvider;

    public LibRawImageProcessor(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
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

    public bool GenerateThumbnail(string imagePath, int width, int height, Span<byte> pixels)
    {
        using var context = RawContext.OpenFile(imagePath);
        try
        {
            using var image = context.ExportThumbnail();

            var thumbnailGenerator = _serviceProvider.GetRequiredKeyedService<IMainThumbnailGenerator>(image.ImageType);
            ArgumentNullException.ThrowIfNull(thumbnailGenerator);
            return thumbnailGenerator.GenerateThumbnail(image, width, height, pixels);
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