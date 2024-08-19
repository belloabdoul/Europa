using Core.Entities;
using File = Core.Entities.File;

namespace Core.Interfaces;

public interface ISimilarFilesFinder
{
    int DegreeOfSimilarity { set; }

    Task<IEnumerable<IGrouping<HashKey, File>>> FindSimilarFilesAsync(string[] hypotheticalDuplicates,
        CancellationToken token = default);
}