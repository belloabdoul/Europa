using System.Text.Json.Serialization;
using U8;
using U8.Serialization;

namespace Core.Entities;

public struct Similarity : IEquatable<Similarity>
{
    [JsonConverter(typeof(U8StringJsonConverter))]
    public U8String OriginalId { get; set; }

    [JsonConverter(typeof(U8StringJsonConverter))]
    public U8String DuplicateId { get; set; }

    public double Score { get; set; }

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