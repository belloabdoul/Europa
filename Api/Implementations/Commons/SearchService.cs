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
    public async Task<IEnumerable<IGrouping<byte[], File>>> SearchAsync(string[] hypotheticalDuplicates,
        FileSearchType searchType, decimal degreeOfSimilarity = 0, CancellationToken cancellationToken = default)
    {
        switch (searchType)
        {
            case FileSearchType.All:
                return await duplicateByHashFinder.FindSimilarFilesAsync(hypotheticalDuplicates,
                    cancellationToken: cancellationToken);
            case FileSearchType.Audios:
                return await similarAudiosFinder.FindSimilarFilesAsync(hypotheticalDuplicates, degreeOfSimilarity,
                    cancellationToken: cancellationToken);
            case FileSearchType.Images:
            default:
                return await similarImagesFinder.FindSimilarFilesAsync(hypotheticalDuplicates, degreeOfSimilarity,
                    cancellationToken);
        }
    }
}