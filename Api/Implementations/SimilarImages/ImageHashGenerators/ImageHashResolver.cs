using Core.Entities.SearchParameters;
using Core.Interfaces.SimilarImages;

namespace Api.Implementations.SimilarImages.ImageHashGenerators;

public class ImageHashResolver : IImageHashResolver
{
    private readonly IServiceProvider _serviceProvider;

    public ImageHashResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IImageHash GetImageHashGenerator(PerceptualHashAlgorithm hashAlgorithm)
    {
        var imageHashGenerator = _serviceProvider.GetRequiredKeyedService<IImageHash>(hashAlgorithm);
        ArgumentNullException.ThrowIfNull(imageHashGenerator);
        return imageHashGenerator;
    }
}