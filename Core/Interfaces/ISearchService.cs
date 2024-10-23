using Core.Entities;
using File = Core.Entities.File;

namespace Core.Interfaces;

public interface ISearchService
{
    Task<IEnumerable<IGrouping<byte[], File>>> SearchAsync(string[] hypotheticalDuplicates, FileSearchType searchType,
        PerceptualHashAlgorithm? perceptualHashAlgorithm, int degreeOfSimilarity = 0,
        CancellationToken cancellationToken = default);
}