using Core.Entities.SearchParameters;
using Core.Interfaces.Commons;
using File = Core.Entities.Files.File;

namespace Api.Implementations.Commons;

public class SearchService : ISearchService
{
    // Search implementations
    private readonly ISimilarFilesFinder _duplicateByHashFinder;
    private readonly ISimilarFilesFinder _similarAudiosFinder;
    private readonly ISimilarFilesFinder _similarImagesFinder;


    public SearchService([FromKeyedServices(FileSearchType.All)] ISimilarFilesFinder duplicateByHashFinder,
        [FromKeyedServices(FileSearchType.Audios)]
        ISimilarFilesFinder similarAudiosFinder,
        [FromKeyedServices(FileSearchType.Images)]
        ISimilarFilesFinder similarImagesFinder)
    {
        _duplicateByHashFinder = duplicateByHashFinder;

        _similarAudiosFinder = similarAudiosFinder;

        _similarImagesFinder = similarImagesFinder;
    }

    public async Task<IEnumerable<IGrouping<byte[], File>>> SearchAsync(string[] hypotheticalDuplicates,
        FileSearchType searchType, PerceptualHashAlgorithm? perceptualHashAlgorithm, decimal degreeOfSimilarity = 0,
        CancellationToken cancellationToken = default)
    {
        switch (searchType)
        {
            case FileSearchType.All:
                return await _duplicateByHashFinder.FindSimilarFilesAsync(hypotheticalDuplicates,
                    cancellationToken: cancellationToken);
            case FileSearchType.Audios:
                return await _similarAudiosFinder.FindSimilarFilesAsync(hypotheticalDuplicates,
                    cancellationToken: cancellationToken);
            case FileSearchType.Images:
            default:
                return await _similarImagesFinder.FindSimilarFilesAsync(hypotheticalDuplicates, perceptualHashAlgorithm,
                    degreeOfSimilarity, cancellationToken);
        }
    }
}