using Core.Entities.Files;

namespace Core.Interfaces.SimilarImages;

public interface IThumbnailGeneratorResolver
{
    IThumbnailGenerator GetThumbnailGenerator(FileType fileType);
}