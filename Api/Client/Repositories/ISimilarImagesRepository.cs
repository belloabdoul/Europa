using Core.Entities.Commons;
using Core.Entities.Images;
using ToolBX.Collections.ObservableDictionary;

namespace Api.Client.Repositories;

public interface ISimilarImagesRepository
{
    ValueTask<ObservableDictionary<long, Similarity>> GetExistingSimilaritiesForImage(long currentGroupId,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<KeyValuePair<long, Similarity>> GetSimilarImages(long id,
        BitArray imageHash, decimal degreeOfSimilarity, IList<long> groupsAlreadyDone,
        CancellationToken cancellationToken = default);

    ValueTask<bool> LinkToSimilarImagesAsync(long id, ICollection<Similarity> newSimilarities,
        CancellationToken cancellationToken = default);
}