namespace Api.Client.Repositories;

public interface IIndexingRepository
{
    ValueTask DisableIndexingAsync(CancellationToken cancellationToken = default);

    ValueTask EnableIndexingAsync(string collectionName, CancellationToken cancellationToken = default);
    
    ValueTask<bool> IsIndexingDoneAsync(CancellationToken cancellationToken = default);
}