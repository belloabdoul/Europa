namespace Api.Client.Repositories;

public interface ICollectionRepository
{
    ValueTask<bool> CollectionExistsAsync(string collectionName, CancellationToken cancellationToken);
    ValueTask CreateCollectionAsync(string collectionName, CancellationToken cancellationToken);
    ValueTask<bool> CreateIndexAsync(string collectionName, string fieldName, CancellationToken cancellationToken);
}