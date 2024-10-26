using Core.Entities;
using Core.Entities.Images;
using Core.Entities.SearchParameters;
using File = Core.Entities.Files.File;

namespace Core.Interfaces.Common;

public interface ISimilarFilesFinder
{
    Task<IEnumerable<IGrouping<byte[], File>>> FindSimilarFilesAsync(string[] hypotheticalDuplicates, PerceptualHashAlgorithm? perceptualHashAlgorithm = null,
        int? degreeOfSimilarity = null, CancellationToken cancellationToken = default);
}