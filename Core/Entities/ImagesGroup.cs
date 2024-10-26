using System.Collections;
using System.Collections.Concurrent;
using NSwag.Collections;

namespace Core.Entities;

public class ImagesGroup
{
    public byte[] Id { get; set; }

    public FileType FileType { get; set; }

    public bool IsCorruptedOrUnsupported { get; set; }

    public long Size { get; set; }

    public DateTime DateModified { get; set; }
    
    public BitArray? ImageHash { get; set; }

    public ConcurrentStack<string> Duplicates { get; } = [];
    
    public ObservableDictionary<byte[], byte>? SimilarImages { get; set; } = [];

    public Similarity[] Similarities { get; set; } = [];
}