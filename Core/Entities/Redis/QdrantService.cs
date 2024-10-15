using Microsoft.Extensions.Hosting;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace Core.Entities.Redis;

public class QdrantService : BackgroundService
{
    private readonly QdrantClient _database;

    public QdrantService(QdrantClient database)
    {
        _database = database;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Check for difference hash
        var collectionExists =
            await _database.CollectionExistsAsync("Europa", stoppingToken);
        if (!collectionExists)
        {
            await _database.CreateCollectionAsync("Europa",
                new VectorParamsMap
                {
                    Map =
                    {
                        [nameof(PerceptualHashAlgorithm.DifferenceHash)] = new VectorParams
                        {
                            Size = 64, Datatype = Datatype.Float16, Distance = Distance.Dot,
                            QuantizationConfig =
                                new QuantizationConfig { Binary = new BinaryQuantization { AlwaysRam = true } }, OnDisk = true
                        },
                        [nameof(PerceptualHashAlgorithm.PerceptualHash)] = new VectorParams
                        {
                            Size = 64, Datatype = Datatype.Float16, Distance = Distance.Dot,
                            QuantizationConfig =
                                new QuantizationConfig { Binary = new BinaryQuantization { AlwaysRam = true } }, OnDisk = true
                        },
                        [nameof(PerceptualHashAlgorithm.BlockMeanHash)] = new VectorParams
                        {
                            Size = 256, Datatype = Datatype.Float16, Distance = Distance.Dot,
                            QuantizationConfig =
                                new QuantizationConfig { Binary = new BinaryQuantization { AlwaysRam = true } }, OnDisk = true
                        }
                    }
                }, cancellationToken: stoppingToken);

            await _database.CreatePayloadIndexAsync(collectionName: "Europa",
                fieldName: nameof(ImagesGroup.Id), cancellationToken: stoppingToken);
        }
    }
}