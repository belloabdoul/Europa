using File = Core.Entities.File;

namespace Core.Interfaces;

public interface ISimilarFilesFinder
{
    Task<IEnumerable<IGrouping<string, File>>> FindSimilarFilesAsync(string[] hypotheticalDuplicates,
        CancellationToken token = default);

    int DegreeOfSimilarity { set; }
}