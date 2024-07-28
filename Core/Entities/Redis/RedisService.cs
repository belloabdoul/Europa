using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Redis.OM;

namespace Core.Entities.Redis;

public class RedisService : IHostedService
{
    private readonly RedisConnectionProvider _provider;

    public RedisService(RedisConnectionProvider provider)
    {
        _provider = provider;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var info = await _provider.Connection.GetIndexInfoAsync(nameof(ImagesGroup));
        if (info == null)
        {
            await _provider.Connection.ExecuteAsync("FT.CREATE", "ImagesGroup", "ON", "Json", "PREFIX", "1",
                "ImagesGroup", "SCHEMA", "$.Id", "AS", "Id", "TAG", "SEPARATOR", "|", "$.ImageHash", "AS", "ImageHash",
                "VECTOR", "FLAT", "6", "TYPE", "FLOAT32", "DIM", "64", "DISTANCE_METRIC", "L2");
        };
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}