using System.Collections;
using System.Collections.Concurrent;
using Core.Entities.Files;
using Core.Entities.Images;
using NSwag.Collections;

namespace Core.Entities.Audios;

public class AudioGroups
{
    public byte[] Id { get; set; }

    public FileType FileType { get; set; }

    public bool IsCorruptedOrUnsupported { get; set; }

    public long Size { get; set; }

    public DateTime DateModified { get; set; }
    
    public BitArray? Fingerprints { get; set; }

    public ConcurrentStack<string> Duplicates { get; } = [];
    
    public ObservableDictionary<byte[], byte>? SimilarImages { get; set; } = [];

    public Similarity[] Similarities { get; set; } = [];
}