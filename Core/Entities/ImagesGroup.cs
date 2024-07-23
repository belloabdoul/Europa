using System.Collections.Concurrent;
using Blake3;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace Core.Entities;

public class ImagesGroup
{
    public ImagesGroup()
    {
    }

    public long Id { get; set; }
    public Hash Hash { get; set; }
    public DateTime DateModified { get; set; }
    public byte[] ImageHash { get; set; }
    public long Size { get; set; }
    public ConcurrentQueue<string> Duplicates { get; } = [];
    public HashSet<long> SimilarImagesGroups { get; set; } = [];
}