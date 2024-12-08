namespace Api.Client.Repositories;

public interface ICollectionRepository
{
    ValueTask CreateCollectionAsync(string collectionName, CancellationToken cancellationToken);
}