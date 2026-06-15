using System.Collections.Concurrent;
using Core.Entities.Commons;
using Core.Entities.Files;
using ToolBX.Collections.ObservableDictionary;

namespace Core.Entities.Images;

public class ImagesGroup
{
    public long Id { get; set; }

    public byte[] FileHash { get; set; } = [];

    public FileType FileType { get; set; }

    public long Size { get; set; }

    public DateTime DateModified { get; set; }

    public BitArray? Hash { get; set; } = new(0, []);

    public ConcurrentStack<string> Duplicates { get; } = [];

    public ObservableDictionary<long, Similarity> Matches { get; set; } = [];
}