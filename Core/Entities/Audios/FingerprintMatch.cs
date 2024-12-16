namespace Core.Entities.Audios;

public struct FingerprintMatch : IEquatable<FingerprintMatch>
{
    public byte[] OriginalId { get; set; }

    public byte[] DuplicateId { get; set; }

    public byte Score { get; set; }

    public float Gap { get; set; }

    public override int GetHashCode()
    {
        return HashCode.Combine(OriginalId, DuplicateId, Score, Gap);
    }

    public override bool Equals(object? obj)
    {
        return obj is FingerprintMatch other && Equals(other);
    }

    public bool Equals(FingerprintMatch other)
    {
        return OriginalId == other.OriginalId && DuplicateId == other.DuplicateId && Score == other.Score &&
               Gap == other.Gap;
    }

    public static bool operator ==(FingerprintMatch left, FingerprintMatch right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(FingerprintMatch left, FingerprintMatch right)
    {
        return !left.Equals(right);
    }
}