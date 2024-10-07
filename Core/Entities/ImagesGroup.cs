using System.Collections.Concurrent;
using Core.Entities.Redis;
using MessagePack;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using U8;

namespace Core.Entities;

[MessagePackObject(keyAsPropertyName: true)]
public class ImagesGroup
{
    [MessagePackFormatter(typeof(U8StringJsonConverter))]
    public U8String Id { get; set; }

    [IgnoreMember]
    public FileType FileType { get; set; }

    [IgnoreMember]
    public bool IsCorruptedOrUnsupported { get; set; }

    [IgnoreMember]
    public long Size { get; set; }

    [IgnoreMember]
    public DateTime DateModified { get; set; }
    
    public Half[]? ImageHash { get; set; }

    [IgnoreMember]
    public ConcurrentStack<string> Duplicates { get; } = [];
    
    [IgnoreMember]
    public ObservableHashSet<U8String> SimilarImages { get; set; } = [];

    public List<Similarity> Similarities { get; set; } = [];
}