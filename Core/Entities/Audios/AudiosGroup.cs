using System.Collections.Concurrent;
using Core.Entities.Commons;
using Swordfish.NET.Collections;

namespace Core.Entities.Audios;

public class AudiosGroup
{
    public byte[] Id { get; set; } = [];

    public bool ToInsert { get; set; }

    public long Size { get; set; }

    public DateTime DateModified { get; set; }

    public IList<Fingerprint>? Fingerprints { get; set; } = [];
    
    public ConcurrentStack<string> Duplicates { get; } = [];

    public ConcurrentObservableDictionary<byte[], Similarity> Matches { get; set; } = [];
    public int FingerprintsCount { get; set; }
}