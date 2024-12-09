using System.Collections.Concurrent;
using Core.Entities.Files;
using NSwag.Collections;

namespace Core.Entities.Images;

public class ImagesGroup
{
    public Guid Id { get; set; }

    public byte[] FileHash { get; set; } = null!;

    public FileType FileType { get; set; }

    public bool IsCorruptedOrUnsupported { get; set; }

    public long Size { get; set; }

    public DateTime DateModified { get; set; }

    public ReadOnlyMemory<Half>? ImageHash { get; set; }

    public ConcurrentStack<string> Duplicates { get; } = [];

    public ObservableDictionary<byte[], Similarity> Similarities { get; set; } = [];
}