using Core.Entities.SearchParameters;

namespace Core.Interfaces.SimilarImages;

public interface IImageHashResolver
{
    IImageHash GetImageHashGenerator(PerceptualHashAlgorithm hashAlgorithm);
}