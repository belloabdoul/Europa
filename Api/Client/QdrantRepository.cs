using System.Collections;
using System.Collections.Concurrent;
using System.IO.Hashing;
using System.Runtime.CompilerServices;
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

public sealed class QdrantRepository : ICollectionRepository, IIndexingRepository, IImagesInfosRepository,
    ISimilarImagesRepository, IDisposable
{
    private readonly QdrantClient _database = new("localhost");
    private static readonly HashComparer HashComparer = new();
    private bool _isDisposed;

    public ValueTask<bool> CollectionExistsAsync(string collectionName, CancellationToken cancellationToken)
    {
        return new ValueTask<bool>(_database.CollectionExistsAsync(collectionName, cancellationToken));
    }

    public ValueTask CreateCollectionAsync(string collectionName, CancellationToken cancellationToken)
    {
        return new ValueTask(_database.CreateCollectionAsync(collectionName, new VectorParamsMap
            {
                Map =
                {
                    [nameof(PerceptualHashAlgorithm.DifferenceHash)] = new VectorParams
                    {
                        Size = 64, Datatype = Datatype.Float16, Distance = Distance.Dot
                    },
                    [nameof(PerceptualHashAlgorithm.PerceptualHash)] = new VectorParams
                    {
                        Size = 64, Datatype = Datatype.Float16, Distance = Distance.Dot
                    },
                    [nameof(PerceptualHashAlgorithm.BlockMeanHash)] = new VectorParams
                    {
                        Size = 961, Datatype = Datatype.Float16, Distance = Distance.Dot
                    }
                }
            }, cancellationToken: cancellationToken, onDiskPayload: true,
            quantizationConfig: new QuantizationConfig { Binary = new BinaryQuantization { AlwaysRam = true } }
        ));
    }

    public async ValueTask<bool> CreateIndexAsync(string collectionName, string fieldName,
        CancellationToken cancellationToken)
    {
        var result =
            await _database.CreatePayloadIndexAsync("Europa", nameof(ImagesGroup.Id),
                cancellationToken: cancellationToken);

        return result.Status == UpdateStatus.Completed;
    }

    public async ValueTask DisableIndexingAsync(CancellationToken cancellationToken)
    {
        await _database.UpdateCollectionAsync("Europa",
            optimizersConfig: new OptimizersConfigDiff { IndexingThreshold = 0 }, cancellationToken: cancellationToken);
    }

    public async ValueTask EnableIndexingAsync(CancellationToken cancellationToken)
    {
        await _database.UpdateCollectionAsync("Europa",
            optimizersConfig: new OptimizersConfigDiff { IndexingThreshold = 1 }, cancellationToken: cancellationToken);
    }

    public async ValueTask<bool> IsIndexingDoneAsync(CancellationToken cancellationToken)
    {
        var infos = await _database.GetCollectionInfoAsync("Europa", cancellationToken: cancellationToken);
        return infos.Status == CollectionStatus.Green;
    }

    public async ValueTask<BitArray?> GetImageInfos(byte[] id, PerceptualHashAlgorithm perceptualHashAlgorithm)
    {
        var images = await _database.RetrieveAsync("Europa", XxHash3.HashToUInt64(id),
            false, true);

        if (images.Count == 0)
            return null;

        var image = images[0];
        if (!image.Vectors.Vectors_.Vectors.TryGetValue(Enum.GetName(perceptualHashAlgorithm)!,
                out var imageHash))
            return new BitArray(0);

        var hash = new BitArray(imageHash.Data.Count);
        for (var i = 0; i < imageHash.Data.Count; i++)
        {
            hash[i] = imageHash.Data[i] > 0;
            i++;
        }

        return hash;
    }

    public async ValueTask<bool> InsertImageInfos(ImagesGroup group, PerceptualHashAlgorithm perceptualHashAlgorithm)
    {
        using var tempVector = MemoryOwner<float>.Allocate(group.ImageHash!.Length);
        Vectorize(group.ImageHash, tempVector.Span);
        var result = await _database.UpsertAsync("Europa", [
            new PointStruct
            {
                Id = XxHash3.HashToUInt64(group.Id),
                Payload =
                {
                    [nameof(ImagesGroup.Id)] = Convert.ToHexStringLower(group.Id),
                    [$"{Enum.GetName(perceptualHashAlgorithm)}{nameof(ImagesGroup.Similarities)}"] =
                        Array.Empty<string>()
                },
                Vectors = new Dictionary<string, Vector>
                {
                    [Enum.GetName(perceptualHashAlgorithm)!] = new()
                        { Data = { MemoryMarshal.ToEnumerable<float>(tempVector.Memory) } }
                }
            }
        ]);

        return result.Status == UpdateStatus.Completed;
    }

    public async ValueTask<bool> AddImageHash(ImagesGroup group, PerceptualHashAlgorithm perceptualHashAlgorithm)
    {
        var result = await _database.SetPayloadAsync("Europa", new Dictionary<string, Value>
        {
            [$"{Enum.GetName(perceptualHashAlgorithm)}{nameof(ImagesGroup.Similarities)}"] =
                Array.Empty<string>()
        }, XxHash3.HashToUInt64(group.Id));

        if (result.Status != UpdateStatus.Completed)
            return false;

        using var tempVector = MemoryOwner<float>.Allocate(group.ImageHash!.Length);
        Vectorize(group.ImageHash, tempVector.Span);
        result = await _database.UpdateVectorsAsync("Europa", [
            new PointVectors
            {
                Id = XxHash3.HashToUInt64(group.Id),
                Vectors = new Dictionary<string, Vector>
                {
                    [Enum.GetName(perceptualHashAlgorithm)!] = new()
                        { Data = { MemoryMarshal.ToEnumerable<float>(tempVector.Memory) } }
                }
            }
        ]);

        return result.Status == UpdateStatus.Completed;
    }

    public async ValueTask<ObservableDictionary<byte[], byte>?> GetSimilarImagesAlreadyDoneInRange(
        byte[] currentGroupId, PerceptualHashAlgorithm perceptualHashAlgorithm)
    {
        var points = await _database.QueryAsync("Europa",
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
            });

        return new ObservableDictionary<byte[], byte>(points[0]
            .Payload[$"{Enum.GetName(perceptualHashAlgorithm)}{nameof(ImagesGroup.Similarities)}"].ListValue
            .Values
            .Select(value =>
                Convert.FromHexString(value.StructValue.Fields[nameof(Similarity.DuplicateId)].StringValue))
            .ToDictionary(val => val, _ => (byte)0), HashComparer);
    }

    public async ValueTask<Similarity[]> GetSimilarImages(byte[] id, BitArray imageHash,
        PerceptualHashAlgorithm perceptualHashAlgorithm, int hashSize,
        int degreeOfSimilarity, ICollection<byte[]> groupsAlreadyDone)
    {
        using var tempVector = MemoryOwner<float>.Allocate(imageHash.Length);
        Vectorize(imageHash, tempVector.Span);
        var similarities = await _database.SearchAsync("Europa",
            vector: tempVector.Memory, vectorName: Enum.GetName(perceptualHashAlgorithm),
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
        var result = await _database.SetPayloadAsync("Europa",
            new ConcurrentDictionary<string, Value>
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
            }, filter: MatchKeyword(nameof(ImagesGroup.Id), Convert.ToHexStringLower(id)));

        return result.Status == UpdateStatus.Completed;
    }

    private static void Vectorize(BitArray vector, Span<float> vectorFLoats)
    {
        ref var vectorRef = ref MemoryMarshal.GetReference(vectorFLoats);
        for (nuint i = 0; i < (nuint)vector.Length; i++)
        {
            Unsafe.Add(ref vectorRef, i) = vector[(int)i] ? 1f : -1f;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _database.Dispose();
        _isDisposed = true;
    }
}