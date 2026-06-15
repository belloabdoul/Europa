using System.Security.Cryptography;

namespace Core.Entities.Commons;

public class ByteArrayComparer: IEqualityComparer<byte[]>
{
    private static readonly UInt128 USeed = new(BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)),
        BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)));

    public bool Equals(byte[]? x, byte[]? y)
    {
        if (ReferenceEquals(x, y))
            return true;
        if (x is null || y is null)
            return false;
        return x.AsSpan().SequenceEqual(y);
    }

    public int GetHashCode(byte[] obj)
    {
        return GxHash.GxHash.Hash32(obj, USeed);
    }
}