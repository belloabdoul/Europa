﻿using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Api.DatabaseRepository.Interfaces;
using Core.Entities;
using Core.Entities.Redis;
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
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    public DbHelpers(IDatabase database)
    {
        _database = database;
        _jsonSerializerOptions = new JsonSerializerOptions();
        _jsonSerializerOptions.Converters.Add(new ImageHashJsonConverter());
        _jsonSerializerOptions.Converters.Add(new ObservableHashSetJsonConverter());
        // _options = MessagePackSerializerOptions.Standard.WithResolver(
        //     CompositeResolver.Create(
        //         new List<IMessagePackFormatter> { new ObservableHashSetJsonConverter(), new ImageHashJsonConverter() },
        //         new List<IFormatterResolver>() { StandardResolver.Instance }));
    }

    [SkipLocalsInit]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<byte[]?> GetImageInfos(U8String id, PerceptualHashAlgorithm perceptualHashAlgorithm)
    {
        Span<char> chars = stackalloc char[id.Length];
        Encoding.UTF8.GetChars(U8Marshal.AsSpan(id), chars);
        return new ValueTask<byte[]?>(_database.JSON().GetAsync<byte[]>(
            key: string.Concat(Enum.GetName(perceptualHashAlgorithm), ":", chars),
            path: $"$.{nameof(ImagesGroup.ImageHash)}", _jsonSerializerOptions));
    }

    [SkipLocalsInit]
    public ValueTask<bool> CacheHash(ImagesGroup group, PerceptualHashAlgorithm perceptualHashAlgorithm)
    {
        Span<char> chars = stackalloc char[group.Id.Length];
        Encoding.UTF8.GetChars(U8Marshal.AsSpan(group.Id), chars);
        return new ValueTask<bool>(_database.JSON().SetAsync(
            string.Concat(Enum.GetName(perceptualHashAlgorithm), ":", chars),
            "$", group));
    }

    public ValueTask<ObservableHashSet<U8String>> GetSimilarImagesAlreadyDoneInRange(U8String id,
        PerceptualHashAlgorithm perceptualHashAlgorithm)
    {
        return new ValueTask<ObservableHashSet<U8String>>(_database.JSON().GetAsync<ObservableHashSet<U8String>>(
            key: $"{Enum.GetName(perceptualHashAlgorithm)}:{id}",
            path: $"$.{nameof(ImagesGroup.Similarities)}[*].{nameof(Similarity.DuplicateId)}", _jsonSerializerOptions)!);
    }

    public async Task<List<Similarity>> GetSimilarImages<T>(U8String id, T[] imageHash,
        PerceptualHashAlgorithm perceptualHashAlgorithm, int degreeOfSimilarity,
        IReadOnlyCollection<U8String> groupsAlreadyDone) where T : struct, INumberBase<T>
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
            .AddParam("vector", Vectorize<T>(imageHash))
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

    [SkipLocalsInit]
    private static byte[] Vectorize<T>(ReadOnlySpan<T> imageHash) where T : struct, INumberBase<T>
    {
        Span<Half> tempHash = stackalloc Half[imageHash.Length];
        for (var i = 0; i < imageHash.Length; i++)
        {
            tempHash[i] = Half.CreateChecked(imageHash[i]);
        }

        return MemoryMarshal.AsBytes(tempHash).ToArray();
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