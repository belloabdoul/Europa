using Pgvector;

namespace Core.Interfaces.SimilarImages;

public interface IImageHashGenerator
{
    Vector GenerateImageHash(FileStream fileStream);
}