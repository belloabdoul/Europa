using Core.Entities;
using File = Core.Entities.File;

namespace Core.Interfaces;

public interface ISimilarFilesFinder
{
    Task<IEnumerable<IGrouping<byte[], File>>> FindSimilarFilesAsync(string[] hypotheticalDuplicates, PerceptualHashAlgorithm? perceptualHashAlgorithm = null,
        int? degreeOfSimilarity = null, CancellationToken cancellationToken = default);
}