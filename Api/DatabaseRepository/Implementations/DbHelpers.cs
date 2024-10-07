using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Api.DatabaseRepository.Interfaces;
using Core.Entities;
using Core.Entities.Redis;
using MessagePack;
using MessagePack.Resolvers;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using NRedisStack.RedisStackCommands;
using NRedisStack.Search;
using StackExchange.Redis;
using U8;
using U8.InteropServices;

namespace Api.DatabaseRepository.Implementations;

public class DbHelpers : IDbHelpers
{
    private readonly IDatabase _database;
    private readonly MessagePackSerializerOptions _options;

    public DbHelpers(IDatabase database)
    {
        _database = database;
        _options = MessagePackSerializerOptions.Standard.WithResolver(
            CompositeResolver.Create(new ObservableHashSetJsonConverter()));
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<Half[]?> GetImageInfos(U8String id, PerceptualHashAlgorithm perceptualHashAlgorithm)
    {
        Span<char> chars = stackalloc char[id.Length];
        Encoding.UTF8.GetChars(U8Marshal.AsSpan(id), chars);
        var result = await _database.JSON().GetAsync(
            key: string.Concat(Enum.GetName(perceptualHashAlgorithm), ":", chars),
            path: $"$.{nameof(ImagesGroup.ImageHash)}");

        return result is { IsNull: false, Resp2Type: ResultType.BulkString }
            ? MessagePackSerializer.Deserialize<Half[][]>(MessagePackSerializer.ConvertFromJson(result.ToString()))[0]
            : null;
    }

    [SkipLocalsInit]
    public Task<bool> CacheHash(ImagesGroup group, PerceptualHashAlgorithm perceptualHashAlgorithm)
    {
        Span<char> chars = stackalloc char[group.Id.Length];
        Encoding.UTF8.GetChars(U8Marshal.AsSpan(group.Id), chars);
        return _database.JSON().SetAsync(string.Concat(Enum.GetName(perceptualHashAlgorithm), ":", chars),
            "$", MessagePackSerializer.SerializeToJson(group));
    }

    public async Task<ObservableHashSet<U8String>> GetSimilarImagesAlreadyDoneInRange(U8String id,
        PerceptualHashAlgorithm perceptualHashAlgorithm)
    {
        var result = await _database.JSON().GetAsync(key: $"{Enum.GetName(perceptualHashAlgorithm)}:{id}",
            indent: null, newLine: null, space: null,
            path: $"$.{nameof(ImagesGroup.Similarities)}[*].{nameof(Similarity.DuplicateId)}");

        return MessagePackSerializer.Deserialize<ObservableHashSet<U8String>>(
            MessagePackSerializer.ConvertFromJson(result.ToString()), _options);
    }

    public async Task<List<Similarity>> GetSimilarImages<T>(U8String id, T[] imageHash,
        PerceptualHashAlgorithm perceptualHashAlgorithm, int degreeOfSimilarity,
        IReadOnlyCollection<U8String> groupsAlreadyDone) where T : struct
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

        return (await ftSearchCommands.SearchAsync(Enum.GetName(perceptualHashAlgorithm)!, query)).Documents.Select(
            document =>
                new Similarity
                {
                    OriginalId = id,
                    DuplicateId = U8String.Create((string)document[nameof(ImagesGroup.Id)]!),
                    Score = int.Parse(document[nameof(Similarity.Score)]!)
                }).ToList();
    }

    private static byte[] Vectorize<T>(T[] imageHash) where T : struct
    {
        return MemoryMarshal.AsBytes(imageHash.AsSpan()).ToArray();
    }

    [SuppressMessage("ReSharper", "LoopCanBeConvertedToQuery")]
    public async Task<bool> LinkToSimilarImagesAsync(U8String id, PerceptualHashAlgorithm perceptualHashAlgorithm,
        ICollection<Similarity> newSimilarities)
    {
        var totalAdded = 0L;
        foreach (var newSimilarity in newSimilarities)
        {
            totalAdded += (await _database.JSON().ArrAppendAsync($"{Enum.GetName(perceptualHashAlgorithm)}:{id}",
                $"$.{nameof(ImagesGroup.Similarities)}", newSimilarity))[0]!.Value;
        }

        return totalAdded > 0;
    }
}