using Api.DatabaseRepository.Interfaces;
using Api.Implementations.DuplicatesByHash;
using Api.Implementations.SimilarAudios;
using Api.Implementations.SimilarImages;
using Core.Entities;
using Core.Interfaces;
using Core.Interfaces.Common;
using Microsoft.AspNetCore.SignalR;

namespace Api.Implementations.Common;

public class SearchTypeImplementationFactory : ISearchTypeImplementationFactory
{
    private readonly IAudioHashGenerator _audioHashGenerator;
    private readonly IDbHelpers _dbHelpers;

    private readonly FileTypeIdentifierResolver _fileTypeIdentifierResolver;

    // Search implementations dependencies
    private readonly IHashGenerator _hashGenerator;
    private readonly IImageHash _imageHashGenerator;
    private readonly IHubContext<NotificationHub> _notificationContext;
    private readonly List<IThumbnailGenerator> _thumbnailGenerators;

    // Singleton implementation
    private ISimilarFilesFinder? _duplicateByHashFinder;
    private ISimilarFilesFinder? _similarAudiosFinder;
    private ISimilarFilesFinder? _similarImagesFinder;


    public SearchTypeImplementationFactory(FileTypeIdentifierResolver fileTypeIdentifierResolver,
        IHashGenerator hashGenerator, IAudioHashGenerator audioHashGenerator,
        IEnumerable<IThumbnailGenerator> thumbnailGenerators, IImageHash imageHashGenerator,
        IHubContext<NotificationHub> notificationContext, IDbHelpers dbHelpers)
    {
        _fileTypeIdentifierResolver = fileTypeIdentifierResolver;
        _hashGenerator = hashGenerator;
        _audioHashGenerator = audioHashGenerator;
        _thumbnailGenerators = thumbnailGenerators.ToList();
        _imageHashGenerator = imageHashGenerator;
        _notificationContext = notificationContext;
        _dbHelpers = dbHelpers;
    }

    public ISimilarFilesFinder GetSearchImplementation(FileSearchType searchType, int degreeOfSimilarity = 0)
    {
        switch (searchType)
        {
            case FileSearchType.All:
                return _duplicateByHashFinder ??=
                    new DuplicateByHashFinder(_hashGenerator, _notificationContext);
            case FileSearchType.Audios:
                return _similarAudiosFinder ??= new SimilarAudiosFinder(
                    _fileTypeIdentifierResolver(FileSearchType.Audios),
                    _audioHashGenerator, _hashGenerator);
            case FileSearchType.Images:
            default:
                if (_similarImagesFinder != null)
                    _similarImagesFinder.DegreeOfSimilarity = degreeOfSimilarity;
                else
                    _similarImagesFinder = new SimilarImageFinder(_notificationContext,
                        _fileTypeIdentifierResolver(FileSearchType.Images), _hashGenerator, _thumbnailGenerators,
                        _imageHashGenerator, _dbHelpers) { DegreeOfSimilarity = degreeOfSimilarity };
                return _similarImagesFinder;
        }
    }
}