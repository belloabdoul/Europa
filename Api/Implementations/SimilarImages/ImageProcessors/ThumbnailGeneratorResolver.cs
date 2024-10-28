using Core.Entities.Files;
using Core.Interfaces.SimilarImages;

namespace Api.Implementations.SimilarImages.ImageProcessors;

public class ThumbnailGeneratorResolver : IThumbnailGeneratorResolver
{
    private readonly IServiceProvider _serviceProvider;

    public ThumbnailGeneratorResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IThumbnailGenerator GetThumbnailGenerator(FileType fileType)
    {
        var thumbnailGenerator = _serviceProvider.GetRequiredKeyedService<IThumbnailGenerator>(fileType);
        ArgumentNullException.ThrowIfNull(thumbnailGenerator);
        return thumbnailGenerator;
    }
}