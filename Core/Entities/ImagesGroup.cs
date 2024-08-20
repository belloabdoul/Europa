using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Core.Entities.Redis;
using ObservableCollections;

namespace Core.Entities;

public class ImagesGroup
{
    public HashKey Id { get; set; }

    [JsonIgnore] public FileType FileType { get; set; }

    [JsonIgnore] public bool IsCorruptedOrUnsupported { get; set; }

    [JsonIgnore] public long Size { get; set; }

    [JsonIgnore] public DateTime DateModified { get; set; }

    [JsonConverter(typeof(ByteVectorJsonConverter))]
    public byte[]? ImageHash { get; set; }

    [JsonIgnore] public ConcurrentQueue<string> Duplicates { get; } = [];

    [JsonIgnore] public ObservableHashSet<HashKey> SimilarImages { get; set; } = [];

    public List<Similarity> Similarities { get; set; } = [];
}