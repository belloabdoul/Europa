using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Api.DatabaseRepository.Interfaces;
using Core.Entities;
using Grpc.Core;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using U8;
using static Qdrant.Client.Grpc.Conditions;

namespace Api.DatabaseRepository.Implementations;

public class DbHelpersQdrant : IDbHelpers
{
    private readonly QdrantClient _database;

    public DbHelpersQdrant(QdrantClient database)
    {
        _database = database;
    }

    [SkipLocalsInit]
    public async ValueTask<float[]?> GetImageInfos(U8String id, PerceptualHashAlgorithm perceptualHashAlgorithm)
    {
        Span<char> hash = stackalloc char[id.Length];
        Encoding.UTF8.GetChars(id, hash);
        var images = await _database.QueryAsync("Europa", filter: MatchKeyword(nameof(ImagesGroup.Id), $"{hash}"),
            limit: 1,
            vectorsSelector: new WithVectorsSelector
                { Enable = true, Include = new VectorsSelector { Names = { Enum.GetName(perceptualHashAlgorithm) } } });

        if (images.Count == 0)
            return null;

        var image = images[0];
        return !image.Vectors.Vectors_.Vectors.TryGetValue(Enum.GetName(perceptualHashAlgorithm)!,
            out var imageHash)
            ? null
            : imageHash.Data.ToArray();
    }

    [SkipLocalsInit]
    public async ValueTask<bool> CacheHash(ImagesGroup group, PerceptualHashAlgorithm perceptualHashAlgorithm)
    {
        Span<char> hash = stackalloc char[group.Id.Length];
        Encoding.UTF8.GetChars(group.Id, hash);
        return (await _database.UpsertAsync("Europa", [
            new PointStruct
            {
                Id = new PointId { Uuid = Guid.CreateVersion7().ToString() },
                Payload =
                {
                    [nameof(group.Similarities)] = new Value { ListValue = new ListValue() },
                    [nameof(group.Id)] = new Value { StringValue = $"{hash}" }
                },
                Vectors = new Vectors
                {
                    Vectors_ = new NamedVectors
                    {
                        Vectors =
                        {
                            [Enum.GetName(perceptualHashAlgorithm)!] = group.ImageHash!
                        }
                    }
                }
            }
        ])).Status == UpdateStatus.Completed;
    }

    public async ValueTask<ObservableHashSet<U8String>> GetSimilarImagesAlreadyDoneInRange(U8String currentGroupId,
        PerceptualHashAlgorithm perceptualHashAlgorithm)
    {
        Span<char> hash = stackalloc char[currentGroupId.Length];
        Encoding.UTF8.GetChars(currentGroupId, hash);


        return new ObservableHashSet<U8String>((await _database.QueryAsync("Europa",
                filter: MatchKeyword(nameof(ImagesGroup.Id), $"{hash}"),
                limit: 1,
                payloadSelector: new WithPayloadSelector()
                {
                    Enable = true,
                    Include = new PayloadIncludeSelector
                        { Fields = { $"{nameof(ImagesGroup.Similarities)}[].{nameof(Similarity.DuplicateId)}" } }
                }))[0].Payload[$"{nameof(ImagesGroup.Similarities)}"].ListValue
            .Values
            .Select(value => new U8String(value.StructValue.Fields[nameof(Similarity.DuplicateId)].StringValue)));
    }

    public async ValueTask<List<Similarity>> GetSimilarImages(U8String id, float[] imageHash,
        PerceptualHashAlgorithm perceptualHashAlgorithm, int hashSize,
        int degreeOfSimilarity, IReadOnlyCollection<U8String> groupsAlreadyDone)
    {
        var similarities = await _database.QueryAsync("Europa",
            imageHash, null, Enum.GetName(perceptualHashAlgorithm),
            MatchExcept(nameof(ImagesGroup.Id), groupsAlreadyDone.Select(groupId => groupId.ToString()).ToList()),
            degreeOfSimilarity - 1, new SearchParams { Exact = true }, long.MaxValue, 0,
            new WithPayloadSelector { Enable = true, Include = new PayloadIncludeSelector { Fields = { "Id" } } });

        return similarities.Select(value => new Similarity
        {
            OriginalId = id,
            DuplicateId = new U8String(value.Payload[nameof(ImagesGroup.Id)].StringValue),
            Score = value.Score
        }).ToList();
    }

    [SkipLocalsInit]
    public async Task<bool> LinkToSimilarImagesAsync(U8String id, PerceptualHashAlgorithm perceptualHashAlgorithm,
        ICollection<Similarity> newSimilarities)
    {
        Span<char> hash = stackalloc char[id.Length];
        Encoding.UTF8.GetChars(id, hash);
        return (await _database.SetPayloadAsync("Europa", new ConcurrentDictionary<string, Value>()
        {
            [nameof(ImagesGroup.Similarities)] = new Value
            {
                ListValue = new ListValue()
                {
                    Values =
                    {
                        newSimilarities.Select(val => new Value()
                        {
                            StructValue = new Struct()
                            {
                                Fields =
                                {
                                    [nameof(val.OriginalId)] = val.OriginalId.ToString(),
                                    [nameof(val.DuplicateId)] = val.DuplicateId.ToString(),
                                    [nameof(val.Score)] = val.Score
                                }
                            }
                        })
                    }
                }
            },
        }, filter: MatchKeyword(nameof(ImagesGroup.Id), $"{hash}"))).Status == UpdateStatus.Completed;
    }
}