using Api.Implementations.DuplicatesByHash;
using Api.Implementations.SimilarAudios;
using Api.Implementations.SimilarImages;
using Core.Entities;
using Core.Interfaces;
using File = Core.Entities.File;

namespace Api.Implementations.Common;

public class SearchService : ISearchService
{
    // Search implementations
    private readonly ISimilarFilesFinder _duplicateByHashFinder;
    private readonly ISimilarFilesFinder _similarAudiosFinder;
    private readonly ISimilarFilesFinder _similarImagesFinder;


    public SearchService(IEnumerable<ISimilarFilesFinder> searchImplementations)
    {
        _duplicateByHashFinder =
            searchImplementations.First(implementation => implementation.GetType() == typeof(DuplicateByHashFinder));

        _similarAudiosFinder =
            searchImplementations.First(implementation => implementation.GetType() == typeof(SimilarAudiosFinder));

        _similarImagesFinder =
            searchImplementations.First(implementation => implementation.GetType() == typeof(SimilarImageFinder));
    }

    public async Task<IEnumerable<IGrouping<byte[], File>>> SearchAsync(string[] hypotheticalDuplicates,
        FileSearchType searchType, PerceptualHashAlgorithm? perceptualHashAlgorithm, int degreeOfSimilarity = 0,
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