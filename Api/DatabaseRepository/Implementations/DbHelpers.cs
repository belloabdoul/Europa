using System.Collections;
using System.Collections.Concurrent;
using Api.DatabaseRepository.Interfaces;
using Core.Entities;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using static Qdrant.Client.Grpc.Conditions;

namespace Api.DatabaseRepository.Implementations;

public class DbHelpers : IDbHelpers
{
    private readonly QdrantClient _database;

    public DbHelpers(QdrantClient database)
    {
        _database = database;
    }
    
    public async ValueTask EnableIndexing(CancellationToken cancellationToken)
    {
        await _database.UpdateCollectionAsync("Europa",
            optimizersConfig: new OptimizersConfigDiff { IndexingThreshold = 1 }, cancellationToken: cancellationToken);

        bool isIndexingCompleted;
        do
        {
            isIndexingCompleted =
                (await _database.GetCollectionInfoAsync("Europa", cancellationToken: cancellationToken)).Status ==
                CollectionStatus.Green;
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        } while (!isIndexingCompleted);
    }

    public async ValueTask DisableIndexing(CancellationToken cancellationToken)
    {
        await _database.UpdateCollectionAsync("Europa",
            optimizersConfig: new OptimizersConfigDiff { IndexingThreshold = 0 }, cancellationToken: cancellationToken);
    }

    public static float[] Vectorize(BitArray vector)
    {
        var vectorFloats = GC.AllocateUninitializedArray<float>(vector.Count);

        nuint i = 0;
        foreach (bool bit in vector)
        {
            vectorFloats[i] = bit ? 1 : -1;
            i++;
        }

        return vectorFloats;
    }

    public async ValueTask<(Guid? Uuid, BitArray? ImageHash)> GetImageInfos(byte[] id,
        PerceptualHashAlgorithm perceptualHashAlgorithm)
    {
        var images = await _database.QueryAsync("Europa",
            filter: MatchKeyword(nameof(ImagesGroup.Id), Convert.ToHexStringLower(id)),
            limit: 1,
            vectorsSelector: new WithVectorsSelector
                { Enable = true, Include = new VectorsSelector { Names = { Enum.GetName(perceptualHashAlgorithm) } } });

        if (images.Count == 0)
            return (null, null);

        var image = images[0];
        if (!image.Vectors.Vectors_.Vectors.TryGetValue(Enum.GetName(perceptualHashAlgorithm)!,
                out var imageHash))
            return (Guid.Parse(image.Id.Uuid), null);

        var tempHashBits = GC.AllocateUninitializedArray<bool>(imageHash.Data.Count);
        nuint i = 0;
        foreach (var bit in imageHash.Data)
        {
            tempHashBits[i] = bit > 0;
            i++;
        }

        return (Guid.Parse(image.Id.Uuid), new BitArray(tempHashBits));
    }

    public async ValueTask<bool> InsertImageInfos(ImagesGroup group, PerceptualHashAlgorithm perceptualHashAlgorithm)
    {
        return (await _database.UpsertAsync("Europa", [
            new PointStruct
            {
                Id = Guid.CreateVersion7(),
                Payload =
                {
                    [$"{Enum.GetName(perceptualHashAlgorithm)}{nameof(ImagesGroup.Similarities)}"] =
                        Array.Empty<string>(),
                    [nameof(group.Id)] = Convert.ToHexStringLower(group.Id)
                },
                Vectors = new Dictionary<string, float[]>
                {
                    [Enum.GetName(perceptualHashAlgorithm)!] = Vectorize(group.ImageHash!)
                }
            }
        ])).Status == UpdateStatus.Completed;
    }

    public async ValueTask<bool> AddImageHash(Guid uuid, ImagesGroup group,
        PerceptualHashAlgorithm perceptualHashAlgorithm)
    {
        var done = (await _database.SetPayloadAsync("Europa", new Dictionary<string, Value>
        {
            [$"{Enum.GetName(perceptualHashAlgorithm)}{nameof(ImagesGroup.Similarities)}"] =
                Array.Empty<string>()
        }, uuid)).Status == UpdateStatus.Completed;

        if (!done)
            return false;

        done = (await _database.UpdateVectorsAsync("Europa", [
            new PointVectors
            {
                Id = uuid,
                Vectors = new Dictionary<string, float[]>
                {
                    [Enum.GetName(perceptualHashAlgorithm)!] = Vectorize(group.ImageHash!)
                }
            }
        ])).Status == UpdateStatus.Completed;

        return done;
    }

    public async ValueTask<ObservableHashSet<byte[]>?> GetSimilarImagesAlreadyDoneInRange(byte[] currentGroupId,
        PerceptualHashAlgorithm perceptualHashAlgorithm)
    {
        return new ObservableHashSet<byte[]>((await _database.QueryAsync("Europa",
                filter: MatchKeyword(nameof(ImagesGroup.Id), Convert.ToHexStringLower(currentGroupId)),
                limit: 1,
                payloadSelector: new WithPayloadSelector
                {
                    Enable = true,
                    Include = new PayloadIncludeSelector
                    {
                        Fields =
                        {
                            $"{Enum.GetName(perceptualHashAlgorithm)}{nameof(ImagesGroup.Similarities)}[].{nameof(Similarity.DuplicateId)}"
                        }
                    }
                }))[0].Payload[$"{Enum.GetName(perceptualHashAlgorithm)}{nameof(ImagesGroup.Similarities)}"].ListValue
            .Values
            .Select(value =>
                Convert.FromHexString(value.StructValue.Fields[nameof(Similarity.DuplicateId)].StringValue)),
            new HashComparer());
    }

    public async ValueTask<Similarity[]> GetSimilarImages(byte[] id, BitArray imageHash,
        PerceptualHashAlgorithm perceptualHashAlgorithm, int hashSize, int degreeOfSimilarity,
        IReadOnlyCollection<byte[]> groupsAlreadyDone)
    {
        var similarities = await _database.SearchAsync("Europa",
            vector: Vectorize(imageHash), vectorName: Enum.GetName(perceptualHashAlgorithm),
            scoreThreshold: hashSize - degreeOfSimilarity - 1, offset: 0, limit: 100,
            filter: MatchExcept(nameof(ImagesGroup.Id),
                groupsAlreadyDone.Select(Convert.ToHexStringLower).ToList()),
            searchParams: new SearchParams { Exact = false },
            payloadSelector: new WithPayloadSelector
                { Enable = true, Include = new PayloadIncludeSelector { Fields = { "Id" } } }
        );

        return similarities.Select(value => new Similarity
        {
            OriginalId = id,
            DuplicateId = Convert.FromHexString(value.Payload[nameof(ImagesGroup.Id)].StringValue),
            Score = Convert.ToDecimal(value.Score)
        }).ToArray();
    }

    public async ValueTask<bool> LinkToSimilarImagesAsync(byte[] id, PerceptualHashAlgorithm perceptualHashAlgorithm,
        Similarity[] newSimilarities)
    {
        return (await _database.SetPayloadAsync("Europa", new ConcurrentDictionary<string, Value>
               {
                   [$"{Enum.GetName(perceptualHashAlgorithm)}{nameof(ImagesGroup.Similarities)}"] = new()
                   {
                       ListValue = new ListValue
                       {
                           Values =
                           {
                               newSimilarities.Select(val => new Value
                               {
                                   StructValue = new Struct
                                   {
                                       Fields =
                                       {
                                           [nameof(val.OriginalId)] = Convert.ToHexStringLower(val.OriginalId),
                                           [nameof(val.DuplicateId)] = Convert.ToHexStringLower(val.DuplicateId),
                                           [nameof(val.Score)] = Convert.ToInt32(val.Score)
                                       }
                                   }
                               })
                           }
                       }
                   },
               }, filter: MatchKeyword(nameof(ImagesGroup.Id), Convert.ToHexStringLower(id)))).Status ==
               UpdateStatus.Completed;
    }
}