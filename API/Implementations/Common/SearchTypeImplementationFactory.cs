using API.Implementations.DuplicatesByHash;
using API.Implementations.SimilarAudios;
using API.Implementations.SimilarImages;
using Core.Entities;
using Core.Interfaces;
using Core.Interfaces.Common;
using Database.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace API.Implementations.Common;

public class SearchTypeImplementationFactory : ISearchTypeImplementationFactory
{
    // Search implementations dependencies
    private readonly IFileReader _fileReader;
    private readonly IHashGenerator _hashGenerator;
    private readonly IHubContext<NotificationHub> _notificationContext;
    private readonly IFileTypeIdentifier _fileTypeIdentifier;
    private readonly IAudioHashGenerator _audioHashGenerator;
    private readonly IImageHash _imageHashGenerator;
    private readonly IDbHelpers _dbHelpers;

    // Singleton implementation
    private ISimilarFilesFinder? _duplicateByHashFinder;
    private ISimilarFilesFinder? _similarAudiosFinder;
    private ISimilarFilesFinder? _similarImagesFinder;


    public SearchTypeImplementationFactory(IFileReader fileReader, IFileTypeIdentifier fileTypeIdentifier,
        IHashGenerator hashGenerator,
        IAudioHashGenerator audioHashGenerator, IHubContext<NotificationHub> notificationContext,
        IImageHash imageHashGenerator, IDbHelpers dbHelpers)
    {
        _fileReader = fileReader;
        _fileTypeIdentifier = fileTypeIdentifier;
        _hashGenerator = hashGenerator;
        _audioHashGenerator = audioHashGenerator;
        _notificationContext = notificationContext;
        _imageHashGenerator = imageHashGenerator;
        _dbHelpers = dbHelpers;
    }

    public ISimilarFilesFinder GetSearchImplementation(FileSearchType searchType, double degreeOfSimilarity = 0)
    {
        switch (searchType)
        {
            case FileSearchType.All:
                return _duplicateByHashFinder ??=
                    new DuplicateByHashFinder(_fileReader, _hashGenerator, _notificationContext);
            case FileSearchType.Audios:
                return _similarAudiosFinder ??= new SimilarAudiosFinder(_fileReader, _fileTypeIdentifier,
                    _audioHashGenerator, _hashGenerator);
            case FileSearchType.Images:
            default:
                return _similarImagesFinder ??= new SimilarImageFinder(degreeOfSimilarity, _notificationContext, _fileReader,
                    _fileTypeIdentifier, _hashGenerator, _imageHashGenerator, _dbHelpers);
        }
    }
}