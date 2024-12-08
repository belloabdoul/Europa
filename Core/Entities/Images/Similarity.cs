using System.IO.Hashing;

namespace Core.Entities.Images;

public struct Similarity : IEquatable<Similarity>
{
    public byte[] OriginalId { get; set; }

    public byte[] DuplicateId { get; set; }

    public decimal Distance { get; set; }

    public override int GetHashCode()
    {
        return HashCode.Combine((int)XxHash3.HashToUInt64(OriginalId), (int)XxHash3.HashToUInt64(DuplicateId), Distance);
    }

    public override bool Equals(object? obj)
    {
        return obj is Similarity other && Equals(other);
    }

    public bool Equals(Similarity other)
    {
        return (ReferenceEquals(OriginalId, other.OriginalId) ||
                OriginalId.AsSpan().SequenceEqual(other.OriginalId)) &&
               (ReferenceEquals(DuplicateId, other.DuplicateId) ||
                OriginalId.AsSpan().SequenceEqual(DuplicateId)) &&
               Distance == other.Distance;
    }

    public static bool operator ==(Similarity left, Similarity right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Similarity left, Similarity right)
    {
        return !left.Equals(right);
    }
}