using Blake3;

namespace Core.Entities;

public readonly struct Similarity : IEquatable<Similarity>
{
    public Hash OriginalId { get; init; }

    public Hash DuplicateId { get; init; }

    public int Score { get; init; }

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
        return OriginalId.Equals(other.OriginalId) && DuplicateId.Equals(other.DuplicateId) && Score == other.Score;
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