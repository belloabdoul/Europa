using Core.Entities.Redis;
using MessagePack;
using U8;

namespace Core.Entities;

[MessagePackObject(keyAsPropertyName: true)]
public struct Similarity : IEquatable<Similarity>
{
    [MessagePackFormatter(typeof(U8StringJsonConverter))]
    public U8String OriginalId { get; set; }

    [MessagePackFormatter(typeof(U8StringJsonConverter))]
    public U8String DuplicateId { get; set; }

    public int Score { get; set; }

    public override int GetHashCode()
    {
        return HashCode.Combine(OriginalId, DuplicateId, Score);
    }

    public override bool Equals(object? obj)
    {
        return obj is Similarity other && Equals(other);
    }

    public bool Equals(Similarity other)
    {
        return OriginalId == other.OriginalId && DuplicateId == other.DuplicateId &&
               Score == other.Score;
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