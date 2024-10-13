using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Core.Entities.Redis;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using U8;

namespace Core.Entities;

public class ImagesGroup
{
    [JsonConverter(typeof(U8StringJsonConverter))]
    public U8String Id { get; set; }

    [JsonIgnore]
    public FileType FileType { get; set; }

    [JsonIgnore]
    public bool IsCorruptedOrUnsupported { get; set; }

    [JsonIgnore]
    public long Size { get; set; }

    [JsonIgnore]
    public DateTime DateModified { get; set; }
    
    [JsonConverter(typeof(ImageHashJsonConverter))]
    public byte[]? ImageHash { get; set; }

    [JsonIgnore]
    public ConcurrentStack<string> Duplicates { get; } = [];
    
    [JsonIgnore]
    public ObservableHashSet<U8String>? SimilarImages { get; set; } = [];

    public List<Similarity> Similarities { get; set; } = [];
}