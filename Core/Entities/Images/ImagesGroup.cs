using System.Collections.Concurrent;
using Core.Entities.Commons;
using Core.Entities.Files;
using Swordfish.NET.Collections;

namespace Core.Entities.Images;

public class ImagesGroup
{
    public byte[] Id { get; set; } = null!;

    public FileType FileType { get; set; }
    
    public long Size { get; set; }

    public DateTime DateModified { get; set; }

    public ReadOnlyMemory<Half>? ImageHash { get; set; }

    public ConcurrentStack<string> Duplicates { get; } = [];

    public ConcurrentObservableDictionary<byte[], Similarity> Similarities { get; set; } = [];
}