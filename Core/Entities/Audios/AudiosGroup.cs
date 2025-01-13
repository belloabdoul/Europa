using System.Collections.Concurrent;
using Core.Entities.Commons;
using Core.Entities.Files;
using Core.Entities.Images;
using NSwag.Collections;
using Swordfish.NET.Collections;

namespace Core.Entities.Audios;

public class AudiosGroup
{
    public byte[] Id { get; set; } = [];

    public FileType FileType { get; set; }

    public bool ToInsert { get; set; }

    public long Size { get; set; }

    public DateTime DateModified { get; set; }

    public IList<Fingerprint>? Fingerprints { get; set; } = [];
    
    public ConcurrentStack<string> Duplicates { get; } = [];

    public ConcurrentDictionary<byte[], ConcurrentDictionary<double, byte>> MatchingFingerprints { get; set; } = [];

    public ConcurrentObservableDictionary<byte[], Similarity> Matches { get; set; } = [];
    public int FingerprintsCount { get; set; }
}