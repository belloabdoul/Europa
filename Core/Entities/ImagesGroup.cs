using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Core.Entities;

public class ImagesGroup
{
    public string Id { get; set; }

    [JsonIgnore]
    public FileType FileType { get; set; }

    [JsonIgnore]
    public bool IsCorruptedOrUnsupported { get; set; }

    [JsonIgnore]
    public long Size { get; set; }

    [JsonIgnore]
    public DateTime DateModified { get; set; }
    
    public Half[]? ImageHash { get; set; }

    [JsonIgnore]
    public ConcurrentStack<string> Duplicates { get; } = [];
    
    [JsonIgnore]
    public ObservableHashSet<string> SimilarImages { get; set; } = [];

    public List<Similarity> Similarities { get; set; } = [];
}