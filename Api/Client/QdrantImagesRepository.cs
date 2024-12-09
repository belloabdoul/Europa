using System.Collections.Concurrent;
using System.IO.Hashing;
using System.Numerics.Tensors;
using System.Runtime.InteropServices;
using Api.Client.Repositories;
using CommunityToolkit.HighPerformance.Buffers;
using Core.Entities.Commons;
using Core.Entities.Images;
using Core.Entities.SearchParameters;
using NSwag.Collections;
using Qdrant.Client;
using Qdrant.Client.Grpc;
using static Qdrant.Client.Grpc.Conditions;

namespace Api.Client;

public sealed class QdrantImagesRepository : ICollectionRepository, IIndexingRepository, IImageInfosRepository,
    ISimilarImagesRepository, IDisposable
{
    private readonly QdrantClient _database = new("localhost");
    private static readonly HashComparer HashComparer = new();
    private bool _isDisposed;

    public async ValueTask CreateCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        var collectionExists = await _database.CollectionExistsAsync(collectionName, cancellationToken);
        if (!collectionExists)
        {
            await _database.CreateCollectionAsync(collectionName, new VectorParamsMap
                {
                    Map =
                    {
                        [nameof(PerceptualHashAlgorithm.QDctHash)] = new VectorParams
                        {
                            Size = 256, Datatype = Datatype.Float16, Distance = Distance.Euclid, OnDisk = true,
                            HnswConfig = new HnswConfigDiff { OnDisk = true }
                        }
                    }
                }, cancellationToken: cancellationToken, onDiskPayload: true
            );

            await _database.CreatePayloadIndexAsync(collectionName, nameof(ImagesGroup.FileHash),
                cancellationToken: cancellationToken);
        }
    }

    public async ValueTask DisableIndexingAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        await _database.UpdateCollectionAsync(collectionName,
            optimizersConfig: new OptimizersConfigDiff { IndexingThreshold = 0 }, cancellationToken: cancellationToken);
    }

    public async ValueTask EnableIndexingAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        await _database.UpdateCollectionAsync(collectionName,
            optimizersConfig: new OptimizersConfigDiff { IndexingThreshold = 1 }, cancellationToken: cancellationToken);
    }

    public async ValueTask<bool> IsIndexingDoneAsync(string collectionName,
        CancellationToken cancellationToken = default)
    {
        var infos = await _database.GetCollectionInfoAsync(collectionName, cancellationToken: cancellationToken);
        return infos.Status == CollectionStatus.Green;
    }

    public async ValueTask<ImageInfos> GetImageInfos(string collectionName, byte[] id,
        PerceptualHashAlgorithm perceptualHashAlgorithm, CancellationToken cancellationToken = default)
    {
        var perceptualHash = Enum.GetName(perceptualHashAlgorithm);
        var images = await _database.QueryAsync(collectionName,
            filter: MatchKeyword(nameof(ImagesGroup.FileHash), Convert.ToHexStringLower(id)),
            vectorsSelector: new WithVectorsSelector
            {
                Enable = true, Include = new VectorsSelector { Names = { perceptualHash } }
            }, limit: 1, cancellationToken: cancellationToken);

        if (images.Count == 0)
            return new ImageInfos(Guid.Empty, Array.Empty<Half>());

        var image = images[0];
        if (!image.Vectors.Vectors_.Vectors.TryGetValue(perceptualHash!, out var imageHash))
            return new ImageInfos(Guid.Parse(image.Id.Uuid), Array.Empty<Half>());

        var hash = GC.AllocateUninitializedArray<Half>(imageHash.Data.Count);

        for (var i = 0; i < imageHash.Data.Count; i++)
        {
            hash[i] = Half.CreateChecked(imageHash.Data[i]);
        }

        return new ImageInfos(Guid.Parse(image.Id.Uuid), hash);
    }

    public async ValueTask<bool> InsertImageInfos(string collectionName, List<ImagesGroup> groups,
        PerceptualHashAlgorithm perceptualHashAlgorithm, CancellationToken cancellationToken = default)
    {
        using var tempVector = MemoryOwner<float>.Allocate(groups[0].ImageHash!.Value.Length);
        var points = new List<PointStruct>(groups.Count);
        foreach (var group in groups)
        {
            TensorPrimitives.ConvertToSingle(group.ImageHash!.Value.Span, tempVector.Span);
            group.Id = Guid.CreateVersion7();
            points.Add(
                new PointStruct
                {
                    Id = group.Id,
                    Payload =
                    {
                        [nameof(ImagesGroup.FileHash)] = Convert.ToHexStringLower(group.FileHash),
                        [$"{Enum.GetName(perceptualHashAlgorithm)}{nameof(ImagesGroup.Similarities)}"] =
                            Array.Empty<string>()
                    },
                    Vectors = new Dictionary<string, Vector>
                    {
                        [Enum.GetName(perceptualHashAlgorithm)!] = new()
                            { Data = { MemoryMarshal.ToEnumerable<float>(tempVector.Memory) } }
                    }
                }
            );
        }

        var result = await _database.UpsertAsync(collectionName, points, cancellationToken: cancellationToken);
        return result.Status == UpdateStatus.Completed;
    }

    public async ValueTask<ObservableDictionary<byte[], Similarity>?> GetExistingSimilaritiesForImage(
        string collectionName, byte[] currentGroupId, PerceptualHashAlgorithm perceptualHashAlgorithm,
        CancellationToken cancellationToken = default)
    {
        var points = await _database.QueryAsync(collectionName,
            filter: MatchKeyword(nameof(ImagesGroup.FileHash), Convert.ToHexStringLower(currentGroupId)),
            limit: 1,
            payloadSelector: new WithPayloadSelector
            {
                Enable = true,
                Include = new PayloadIncludeSelector
                {
                    Fields =
                    {
                        $"{Enum.GetName(perceptualHashAlgorithm)}{nameof(ImagesGroup.Similarities)}[].{nameof(Similarity.DuplicateId)}",
                        $"{Enum.GetName(perceptualHashAlgorithm)}{nameof(ImagesGroup.Similarities)}[].{nameof(Similarity.Distance)}"
                    }
                }
            }, cancellationToken: cancellationToken);

        return new ObservableDictionary<byte[], Similarity>(points[0]
            .Payload[$"{Enum.GetName(perceptualHashAlgorithm)}{nameof(ImagesGroup.Similarities)}"].ListValue
            .Values
            .Select(value =>
                new Similarity
                {
                    OriginalId = currentGroupId,
                    DuplicateId =
                        Convert.FromHexString(value.StructValue.Fields[nameof(Similarity.DuplicateId)].StringValue),
                    Distance = Convert.ToDecimal(value.StructValue.Fields[nameof(Similarity.Distance)].DoubleValue)
                })
            .ToDictionary(val => val.DuplicateId, val => val, HashComparer), HashComparer);
    }

    public async ValueTask<IEnumerable<KeyValuePair<byte[], Similarity>>> GetSimilarImages(string collectionName,
        byte[] id, ReadOnlyMemory<Half> imageHash, PerceptualHashAlgorithm perceptualHashAlgorithm,
        decimal degreeOfSimilarity, ICollection<byte[]> groupsAlreadyDone,
        CancellationToken cancellationToken = default)
    {
        using var tempVector = MemoryOwner<float>.Allocate(imageHash.Length);
        TensorPrimitives.ConvertToSingle(imageHash.Span, tempVector.Span);
        var similarities = await _database.SearchAsync(collectionName,
            vector: tempVector.Memory, vectorName: Enum.GetName(perceptualHashAlgorithm),
            scoreThreshold: Convert.ToSingle(degreeOfSimilarity), offset: 0, limit: 100,
            filter: MatchExcept(nameof(ImagesGroup.FileHash),
                groupsAlreadyDone.Select(Convert.ToHexStringLower).ToList()),
            searchParams: new SearchParams { Exact = true },
            payloadSelector: new WithPayloadSelector
                { Enable = true, Include = new PayloadIncludeSelector { Fields = { nameof(ImagesGroup.FileHash) } } },
            cancellationToken: cancellationToken);

        return similarities.Select(value =>
        {
            var duplicateId = Convert.FromHexString(value.Payload[nameof(ImagesGroup.FileHash)].StringValue);
            return new KeyValuePair<byte[], Similarity>(duplicateId,
                new Similarity
                    { OriginalId = id, DuplicateId = duplicateId, Distance = Convert.ToDecimal(value.Score) });
        });
    }

    public async ValueTask<bool> LinkToSimilarImagesAsync(string collectionName, Guid id,
        PerceptualHashAlgorithm perceptualHashAlgorithm, ICollection<Similarity> similarities,
        CancellationToken cancellationToken = default)
    {
        var stringLength = similarities.First().OriginalId.Length * 2;
        var result = await _database.SetPayloadAsync(collectionName,
            new ConcurrentDictionary<string, Value>
            {
                [$"{Enum.GetName(perceptualHashAlgorithm)}{nameof(ImagesGroup.Similarities)}"] = new()
                {
                    ListValue = new ListValue
                    {
                        Values =
                        {
                            similarities.Select(val =>
                            {
                                using var tempOriginalId =
                                    MemoryOwner<char>.Allocate(similarities.First().OriginalId.Length * 2);
                                using var tempDuplicateId =
                                    MemoryOwner<char>.Allocate(similarities.First().OriginalId.Length * 2);
                                return new Value
                                {
                                    StructValue = new Struct
                                    {
                                        Fields =
                                        {
                                            [nameof(val.OriginalId)] = string.Create(stringLength, 0,
                                                (output, input) =>
                                                {
                                                    Convert.TryToHexStringLower(val.OriginalId, output, out _);
                                                }),
                                            [nameof(val.DuplicateId)] = string.Create(stringLength, 0,
                                                (output, input) =>
                                                {
                                                    Convert.TryToHexStringLower(val.DuplicateId, output, out _);
                                                }),
                                            [nameof(val.Distance)] = Convert.ToDouble(val.Distance)
                                        }
                                    }
                                };
                            })
                        }
                    }
                },
            }, id, cancellationToken: cancellationToken);

        return result.Status == UpdateStatus.Completed;
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _database.Dispose();
        _isDisposed = true;
    }
}