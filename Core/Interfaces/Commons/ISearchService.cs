using Core.Entities.SearchParameters;
using File = Core.Entities.Files.File;

namespace Core.Interfaces.Commons;

public interface ISearchService
{
    Task<IEnumerable<IGrouping<byte[], File>>> SearchAsync(string[] hypotheticalDuplicates, FileSearchType searchType,
        PerceptualHashAlgorithm? perceptualHashAlgorithm, decimal degreeOfSimilarity = 0,
        CancellationToken cancellationToken = default);
}