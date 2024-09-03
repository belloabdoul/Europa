using Blake3;
using File = Core.Entities.File;

namespace Core.Interfaces;

public interface ISimilarFilesFinder
{
    int DegreeOfSimilarity { set; }

    Task<IEnumerable<IGrouping<Hash, File>>> FindSimilarFilesAsync(string[] hypotheticalDuplicates,
        CancellationToken token = default);
}