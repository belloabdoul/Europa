namespace Api.Client.Repositories;

public interface IIndexingRepository
{
    ValueTask DisableIndexingAsync(string collectionName, CancellationToken cancellationToken);

    ValueTask EnableIndexingAsync(string collectionName, CancellationToken cancellationToken);
    
    ValueTask<bool> IsIndexingDoneAsync(string collectionName, CancellationToken cancellationToken);
}