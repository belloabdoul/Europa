namespace Core.Entities;

public class ByteArrayEqualityComparer : IEqualityComparer<byte[]>
{
    public int GetHashCode(byte[] obj) =>
        string.GetHashCode(Convert.ToHexString(obj), StringComparison.OrdinalIgnoreCase);

    public bool Equals(byte[]? x, byte[]? y) =>
        (x == null && y == null) || (x != null && y != null && x.AsSpan().SequenceEqual(y));
}