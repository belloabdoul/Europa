using System.Collections.Concurrent;
using Core.Entities.Commons;
using Core.Entities.Files;
using Core.Entities.Images;
using NSwag.Collections;

namespace Core.Entities.Audios;

public class AudiosGroup
{
    public byte[] Id { get; set; } = [];

    public FileType FileType { get; set; }

    public bool IsCorruptedOrUnsupported { get; set; }

    public long Size { get; set; }

    public DateTime DateModified { get; set; }
    
    public int FingerprintsCount { get; set; }
    
    public ConcurrentStack<string> Duplicates { get; } = [];
    
    public ConcurrentDictionary<byte[], ConcurrentDictionary<double, byte>> MatchingFingerprints { get; set; } = [];
    
    public ObservableDictionary<byte[], Similarity> Matches { get; set; } = [];
}