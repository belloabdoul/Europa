namespace Api.Client.Repositories;

public interface IIndexingRepository
{
    ValueTask DisableIndexingAsync(string collectionName, CancellationToken cancellationToken = default);

    ValueTask EnableIndexingAsync(string collectionName, CancellationToken cancellationToken = default);
    
    ValueTask<bool> IsIndexingDoneAsync(string collectionName, CancellationToken cancellationToken = default);
}