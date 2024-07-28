using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Blake3;
using Core.Entities.Redis;
using Redis.OM;
using Redis.OM.Modeling;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace Core.Entities;

[Document(StorageType = StorageType.Json, Prefixes = [nameof(ImagesGroup)], IndexName = nameof(ImagesGroup))]
public class ImagesGroup
{
    [RedisIdField]
    [Indexed]
    public string Id { get; set; }
    
    [JsonIgnore]
    public bool IsCorruptedOrUnsupported { get; set; }
    
    [JsonIgnore]
    public long Size { get; set; }
    
    [JsonIgnore]
    public DateTime DateModified { get; set; }
    
    [Indexed(DistanceMetric = DistanceMetric.L2, Algorithm = VectorAlgorithm.FLAT)]
    [ByteToFloatVectorizer(64)]
    public Vector<byte[]> ImageHash { get; set; }
    
    [JsonIgnore]
    public ConcurrentQueue<string> Duplicates { get; } = [];
    
    [JsonIgnore]
    public HashSet<string> SimilarImages { get; set; } = [];

    public HashSet<Similarity> Similarities { get; set; } = [];
}