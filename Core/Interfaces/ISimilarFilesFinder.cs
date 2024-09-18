using Core.Entities;
using File = Core.Entities.File;

namespace Core.Interfaces;

public interface ISimilarFilesFinder
{
    PerceptualHashAlgorithm PerceptualHashAlgorithm { set; }
    int DegreeOfSimilarity { set; }

    Task<IEnumerable<IGrouping<string, File>>> FindSimilarFilesAsync(string[] hypotheticalDuplicates,
        CancellationToken token = default);
}