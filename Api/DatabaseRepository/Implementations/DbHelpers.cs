using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Api.DatabaseRepository.Interfaces;
using Api.Implementations.SimilarImages.ImageHashGenerators;
using Blake3;
using Core.Entities;
using Core.Entities.Redis;
using NRedisStack;
using NRedisStack.RedisStackCommands;
using NRedisStack.Search;
using ObservableCollections;
using StackExchange.Redis;
using JsonCommandBuilder = NRedisStack.JsonCommandBuilder;

namespace Api.DatabaseRepository.Implementations;

public class DbHelpers : IDbHelpers
{
    private readonly IDatabase _database;
    private static JsonSerializerOptions? _jsonSerializerOptions;

    public DbHelpers(IDatabase database)
    {
        _database = database;
        _jsonSerializerOptions = new JsonSerializerOptions();
        _jsonSerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
        _jsonSerializerOptions.Converters.Insert(0, new HashKeyJsonConverter());
        _jsonSerializerOptions.Converters.Insert(0, new ByteVectorJsonConverter());
    }

    public async Task<byte[]?> GetImageInfosAsync(Hash id)
    {
        var redisResult =
            await _database.ExecuteAsync(JsonCommandBuilder.Get<byte[]>($"{nameof(DifferenceHash)}:{id}",
                $"$.{nameof(ImagesGroup.ImageHash)}"));

        if (redisResult.Type != ResultType.BulkString || redisResult.IsNull)
            return default;

        var jsonArray = JsonSerializer.Deserialize<JsonArray>(redisResult.ToString()!, _jsonSerializerOptions);

        if (jsonArray is { Count: > 0 })
            return JsonSerializer.Deserialize<byte[]>(JsonSerializer.Serialize(jsonArray[0]!, _jsonSerializerOptions),
                _jsonSerializerOptions);

        return default;
    }

    public async Task<bool> CacheHashAsync(ImagesGroup group)
    {
        var jsonCommands = _database.JSON();
        return await jsonCommands.SetAsync($"{nameof(DifferenceHash)}:{group.Id}", "$", group, When.Always,
            _jsonSerializerOptions);
    }

    public async Task<ObservableHashSet<Hash>> GetSimilarImagesAlreadyDoneInRange(Hash id)
    {
        var redisResult =
            await _database.ExecuteAsync(JsonCommandBuilder.Get<byte[]>($"{nameof(DifferenceHash)}:{id}",
                $"$.{nameof(ImagesGroup.Similarities)}[*].{nameof(Similarity.DuplicateId)}"));

        if (redisResult.Type != ResultType.BulkString || redisResult.IsNull)
            return [];

        var jsonArray = JsonSerializer.Deserialize<JsonArray>(redisResult.ToString()!, _jsonSerializerOptions);

        if (jsonArray is not { Count: > 0 })
            return [];

        var result = new ObservableHashSet<Hash>(jsonArray.Count);

        result.AddRange(jsonArray.Select(node => Hash.FromBytes(Convert.FromHexString(node!.ToString()))));

        return result;
    }

    public async Task<List<Similarity>> GetSimilarImages(Hash id, byte[] imageHash,
        int degreeOfSimilarity, IReadOnlyCollection<Hash> groupsAlreadyDone)
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
                DuplicateId = Hash.FromBytes(Convert.FromHexString(document[nameof(ImagesGroup.Id)]!)),
                Score = int.Parse(document[nameof(Similarity.Score)]!)
            }).ToList();
    }

    [SuppressMessage("ReSharper", "LoopCanBeConvertedToQuery")]
    public async Task<bool> LinkToSimilarImagesAsync(Hash id, ICollection<Similarity> newSimilarities)
    {
        var totalAdded = 0L;
        foreach (var newSimilarity in newSimilarities)
        {
            totalAdded += (await ArrAppendAsync($"{nameof(DifferenceHash)}:{id}",
                $"$.{nameof(ImagesGroup.Similarities)}", newSimilarity))[0]!.Value;
        }

        return totalAdded > 0;
    }

    private static byte[] Vectorize(byte[] obj)
    {
        var vectorSize = obj.Length;
        var vector = new byte[vectorSize * sizeof(short)];
        for (var i = 0; i < vectorSize; i++)
        {
            var halfBytes = BinaryToHalfBytes[obj[i]];
            vector[sizeof(short) * i] = halfBytes[0];
            vector[sizeof(short) * i + 1] = halfBytes[1];
        }

        return vector;
    }

    private static readonly Dictionary<byte, byte[]> BinaryToHalfBytes = new()
    {
        {
            0, BitConverter.GetBytes((Half)0)
        },
        {
            1, BitConverter.GetBytes((Half)1)
        }
    };

    // Copied from the class JsonCommandsAsync of NRedisStack with redundant code cleaned. Nothing has been changed
    public async Task<long?[]> ArrAppendAsync(RedisKey key, string? path = null, params object[] values)
    {
        return (await _database.ExecuteAsync(ArrAppend(key, path, values))).ToNullableLongArray();
    }

    // Copied from the class JsonCommandBuilder of NRedisStack with redundant code cleaned
    // Create the command to send to redis. Here we pass our custom json serializer options since without it in AOT
    // runtime an exception is throw for forbidden reflection-based serialization
    public static SerializedCommand ArrAppend(RedisKey key, string? path = null, params object[] values)
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
            values.Select((Func<object, string>)(x => JsonSerializer.Serialize(x, _jsonSerializerOptions))));
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
        return result.Type != ResultType.Integer
            ? ((IEnumerable<RedisResult>)(RedisResult[])result)
            .Select((Func<RedisResult, long?>)(x => (long?)x)).ToArray()
            : [(long?)result];
    }
}