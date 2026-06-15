using System.Collections;
using System.Security.Cryptography;

namespace Core.Entities.Commons;

public class ImageHashComparer : IEqualityComparer<BitArray>
{
    private static readonly UInt128 USeed = new(BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)),
        BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)));

    public bool Equals(BitArray? x, BitArray? y)
    {
        if (x is null && y is null)
            return true;
        if (x is null || y is null)
            return false;
        var xValues = new byte[x.Length];
        var yValues = new byte[y.Length];
        return xValues.AsSpan().SequenceEqual(yValues);
    }

    public int GetHashCode(BitArray obj)
    {
        var values = new byte[obj.Length];
        obj.CopyTo(values, 0);
        return GxHash.GxHash.Hash32(values, USeed);
    }
}