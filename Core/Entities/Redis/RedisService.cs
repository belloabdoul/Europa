using Microsoft.Extensions.Hosting;
using NRedisStack.RedisStackCommands;
using NRedisStack.Search;
using NRedisStack.Search.Literals.Enums;
using StackExchange.Redis;

namespace Core.Entities.Redis;

public class RedisService : IHostedService
{
    private readonly IDatabase _database;

    public RedisService(IDatabase database)
    {
        _database = database;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var ftSearchCommands = _database.FT();
        try
        {
            await ftSearchCommands.InfoAsync(nameof(ImagesGroup));
        }
        catch (Exception)
        {
            ftSearchCommands.Create(nameof(ImagesGroup),
                FTCreateParams.CreateParams().AddPrefix("DifferenceHash:").On(IndexDataType.JSON),
                new Schema()
                    .AddTagField(new FieldName($"$.{nameof(ImagesGroup.Id)}", nameof(ImagesGroup.Id)))
                    .AddVectorField(new FieldName($"$.{nameof(ImagesGroup.ImageHash)}", nameof(ImagesGroup.ImageHash)),
                        Schema.VectorField.VectorAlgo.FLAT, new Dictionary<string, object>
                        {
                            { "TYPE", "FLOAT16" },
                            { "DIM", "64" },
                            { "DISTANCE_METRIC", "L2" },
                        }
                    ));
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}