using Core.Entities;
using U8;
using File = Core.Entities.File;

namespace Core.Interfaces;

public interface ISearchService
{
    Task<IEnumerable<IGrouping<U8String, File>>> SearchAsync(string[] hypotheticalDuplicates, FileSearchType searchType,
        PerceptualHashAlgorithm? perceptualHashAlgorithm, int degreeOfSimilarity = 0,
        CancellationToken cancellationToken = default);
}