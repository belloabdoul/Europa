namespace Api.Client.Repositories;

public interface IIndexingRepository
{
    ValueTask DisableIndexingAsync(CancellationToken cancellationToken = default);

    ValueTask EnableIndexingAsync(CancellationToken cancellationToken = default);
    
    ValueTask<bool> IsIndexingDoneAsync(CancellationToken cancellationToken = default);
}