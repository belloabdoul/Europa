using Core.Entities;
using U8;
using File = Core.Entities.File;

namespace Core.Interfaces;

public interface ISimilarFilesFinder
{
    Task<IEnumerable<IGrouping<U8String, File>>> FindSimilarFilesAsync(string[] hypotheticalDuplicates, PerceptualHashAlgorithm? perceptualHashAlgorithm = null,
        int? degreeOfSimilarity = null, CancellationToken cancellationToken = default);
}