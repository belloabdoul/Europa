using Blake3;
using File = Core.Entities.File;

namespace Core.Interfaces.SimilarImages;

public interface ISimilarImagesFinder
{
    Task<IEnumerable<IGrouping<Hash, File>>> FindSimilarImagesAsync(HashSet<string> hypotheticalDuplicates,
        double degreeOfSimilarity, CancellationToken token);
}