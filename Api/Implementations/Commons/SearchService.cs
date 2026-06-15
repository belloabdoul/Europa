using Core.Entities.SearchParameters;
using Core.Interfaces.Commons;
using File = Core.Entities.Files.File;

namespace Api.Implementations.Commons;

public class SearchService(
    [FromKeyedServices(FileSearchType.All)]
    ISimilarFilesFinder duplicateByHashFinder,
    [FromKeyedServices(FileSearchType.Audios)]
    ISimilarFilesFinder similarAudiosFinder,
    [FromKeyedServices(FileSearchType.Images)]
    ISimilarFilesFinder similarImagesFinder)
    : ISearchService
{
    public Task<IEnumerable<IGrouping<byte[], File>>> SearchAsync(List<string> hypotheticalDuplicates,
        FileSearchType searchType, decimal degreeOfSimilarity = 0, CancellationToken cancellationToken = default)
    {
        switch (searchType)
        {
            case FileSearchType.All:
                return duplicateByHashFinder.FindSimilarFilesAsync(hypotheticalDuplicates,
                    cancellationToken: cancellationToken);
            case FileSearchType.Audios:
                return similarAudiosFinder.FindSimilarFilesAsync(hypotheticalDuplicates, degreeOfSimilarity,
                    cancellationToken: cancellationToken);
            case FileSearchType.Images:
            default:
                return similarImagesFinder.FindSimilarFilesAsync(hypotheticalDuplicates, degreeOfSimilarity,
                    cancellationToken);
        }
    }
}