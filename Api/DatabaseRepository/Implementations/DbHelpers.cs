using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Api.DatabaseRepository.Interfaces;
using Api.Implementations.SimilarImages.ImageHashGenerators;
using CommunityToolkit.HighPerformance.Buffers;
using Core.Entities;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using NRedisStack;
using NRedisStack.RedisStackCommands;
using NRedisStack.Search;
using StackExchange.Redis;

namespace Api.DatabaseRepository.Implementations;

public class DbHelpers : IDbHelpers
{
    private readonly IDatabase _database;

    public DbHelpers(IDatabase database)
    {
        _database = database;
    }

    public async ValueTask<Half[]?> GetImageInfosAsync(string id)
    {
        var redisResult = await _database.JSON().GetAsync(key: $"{nameof(DifferenceHash)}:{id}", indent: null,
            newLine: null, space: null, $"$.{nameof(ImagesGroup.ImageHash)}");

        if (redisResult.Resp2Type != ResultType.BulkString || redisResult.IsNull)
            return default;

        var jsonArray =
            JsonSerializer.Deserialize<JsonArray>(redisResult.ToString(), AppJsonSerializerContext.Default.JsonArray);

        if (jsonArray is { Count: > 0 })
            return JsonSerializer.Deserialize<Half[]>(
                JsonSerializer.Serialize<JsonNode>(jsonArray[0]!, AppJsonSerializerContext.Default.JsonNode),
                AppJsonSerializerContext.Default.HalfArray);

        return default;
    }

    public Task<bool> CacheHashAsync(ImagesGroup group)
    {
        var jsonCommands = _database.JSON();
        return jsonCommands.SetAsync($"{nameof(DifferenceHash)}:{group.Id}", "$", group, When.Always,
            AppJsonSerializerContext.Default.Options);
    }

    public async Task<ObservableHashSet<string>> GetSimilarImagesAlreadyDoneInRange(string id)
    {
        var redisResult = await _database.JSON().GetAsync(key: $"{nameof(DifferenceHash)}:{id}", indent: null,
            newLine: null, space: null,
            path: $"$.{nameof(ImagesGroup.Similarities)}[*].{nameof(Similarity.DuplicateId)}");

        if (redisResult.Resp2Type != ResultType.BulkString || redisResult.IsNull)
            return [];

        var jsonArray =
            JsonSerializer.Deserialize<JsonArray>(redisResult.ToString(), AppJsonSerializerContext.Default.JsonArray);

        if (jsonArray is not { Count: > 0 })
            return [];

        var result =
            new ObservableHashSet<string>(jsonArray.Select(node => StringPool.Shared.GetOrAdd(node!.ToString())));

        return result;
    }

    public async Task<List<Similarity>> GetSimilarImages<T>(string id, T[] imageHash,
        int degreeOfSimilarity, IReadOnlyCollection<string> groupsAlreadyDone) where T : struct
    {
        var queryBuilder = new StringBuilder();

        if (groupsAlreadyDone.Count > 0)
        {
            queryBuilder.Append($"-@{nameof(ImagesGroup.Id)}:");
            queryBuilder.Append('{');
            var i = 0;
            foreach (var group in groupsAlreadyDone)
            {
                queryBuilder.Append(group);
                if (i != groupsAlreadyDone.Count - 1)
                    queryBuilder.Append('|');
                i++;
            }

            queryBuilder.Append("} ");
        }

        queryBuilder.Append("@ImageHash:[VECTOR_RANGE $distance $vector]=>{$YIELD_DISTANCE_AS: Score}");

        var query = new Query(queryBuilder.ToString())
            .AddParam("distance", degreeOfSimilarity)
            .AddParam("vector", Vectorize(imageHash))
            .ReturnFields(nameof(ImagesGroup.Id), nameof(Similarity.Score))
            .Dialect(2);

        query.SortBy = nameof(Similarity.Score);
        var ftSearchCommands = _database.FT();

        return (await ftSearchCommands.SearchAsync(nameof(ImagesGroup), query)).Documents.Select(document =>
            new Similarity
            {
                OriginalId = id,
                DuplicateId = document[nameof(ImagesGroup.Id)]!,
                Score = int.Parse(document[nameof(Similarity.Score)]!)
            }).ToList();
    }

    private static byte[] Vectorize<T>(T[] imageHash) where T : struct
    {
        return MemoryMarshal.AsBytes(imageHash.AsSpan()).ToArray();
    }

    [SuppressMessage("ReSharper", "LoopCanBeConvertedToQuery")]
    public async Task<bool> LinkToSimilarImagesAsync(string id, ICollection<Similarity> newSimilarities)
    {
        var totalAdded = 0L;
        foreach (var newSimilarity in newSimilarities)
        {
            totalAdded += (await ArrAppendAsync($"{nameof(DifferenceHash)}:{id}",
                $"$.{nameof(ImagesGroup.Similarities)}", newSimilarity))[0]!.Value;
        }

        return totalAdded > 0;
    }

    // Copied from the class JsonCommandsAsync of NRedisStack with redundant code cleaned. Nothing has been changed
    public async Task<long?[]> ArrAppendAsync(RedisKey key, string? path = null, params object[] values)
    {
        return (await _database.ExecuteAsync(ArrAppend(key, AppJsonSerializerContext.Default, path, values)))
            .ToNullableLongArray();
    }

    // Copied from the class JsonCommandBuilder of NRedisStack with redundant code cleaned
    // Create the command to send to redis. Here we pass our custom json serializer options since without it in AOT
    // runtime an exception is throw for forbidden reflection-based serialization
    public static SerializedCommand ArrAppend(RedisKey key, JsonSerializerContext jsonSerializerContext,
        string? path = null, params object[] values)
    {
        if (values.Length < 1)
            throw new ArgumentOutOfRangeException(nameof(values));
        var objectList = new List<object>
        {
            key
        };
        if (path != null)
            objectList.Add(path);
        objectList.AddRange(
            values.Select((Func<object, string>)(x =>
                JsonSerializer.Serialize(x, typeof(Similarity), jsonSerializerContext))));
        return new SerializedCommand("JSON.ARRAPPEND", objectList.ToArray());
    }
}

public static class RedisResponseParser
{
    // Copied from the class JsonCommandBuilder of NRedisStack with code cleaned. Nothing has been changed
    // Return the number of value updated per path. Since in our use case only one path is returned, the list will contain
    // only one value
    public static long?[] ToNullableLongArray(this RedisResult result)
    {
        if (result.IsNull)
            return [];
        return result.Resp2Type != ResultType.Integer
            ? ((IEnumerable<RedisResult>)(RedisResult[])result)
            .Select((Func<RedisResult, long?>)(x => (long?)x)).ToArray()
            : [(long?)result];
    }
}