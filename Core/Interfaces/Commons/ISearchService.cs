using Core.Entities;
using Core.Entities.Files;
using Core.Entities.Images;
using Core.Entities.SearchParameters;
using File = Core.Entities.Files.File;

namespace Core.Interfaces.Common;

public interface ISearchService
{
    Task<IEnumerable<IGrouping<byte[], File>>> SearchAsync(string[] hypotheticalDuplicates, FileSearchType searchType,
        PerceptualHashAlgorithm? perceptualHashAlgorithm, int degreeOfSimilarity = 0,
        CancellationToken cancellationToken = default);
}