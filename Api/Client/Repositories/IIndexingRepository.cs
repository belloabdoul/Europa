namespace Api.Client.Repositories;

public interface IIndexingRepository
{
    ValueTask DisableIndexingAsync(CancellationToken cancellationToken);

    ValueTask EnableIndexingAsync(CancellationToken cancellationToken);
    
    ValueTask<bool> IsIndexingDoneAsync(CancellationToken cancellationToken);
}