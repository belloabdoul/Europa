using Api.Client.Repositories;
using Core.Entities.Images;

namespace Api.Client;

public class QdrantConfig : BackgroundService
{
    private readonly ICollectionRepository _database;

    public QdrantConfig(ICollectionRepository database)
    {
        _database = database;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!await _database.CollectionExistsAsync("Europa", stoppingToken))
        {
            await _database.CreateCollectionAsync("Europa", stoppingToken);

            await _database.CreateIndexAsync("Europa", nameof(ImagesGroup.Id), stoppingToken);
        }
    }
}