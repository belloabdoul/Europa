using Redis.OM.Modeling;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace Core.Entities;

[Document(StorageType = StorageType.Json)]
public class Similarity : IEquatable<Similarity>
{
    public string OriginalId { get; init; }

    public string DuplicateId { get; init; }

    public double Score { get; init; }

    public bool Equals(Similarity? other)
    {
        return other is not null &&
               ((OriginalId == other.OriginalId && DuplicateId == other.DuplicateId) ||
                (OriginalId == other.DuplicateId && DuplicateId == other.OriginalId)) && Score.Equals(other.Score);
    }

    public override bool Equals(object? obj)
    {
        return obj is Similarity similarity && Equals(similarity);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(OriginalId, DuplicateId, Score);
    }

    public static bool operator ==(Similarity? left, Similarity? right)
    {
        return (left is null && right is null) || (left is not null && left.Equals(right));
    }

    public static bool operator !=(Similarity? left, Similarity? right)
    {
        return !(left == right);
    }
}