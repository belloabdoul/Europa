using System.Runtime.Intrinsics;
using System.Text.Json.Serialization;
using Core.Entities.Redis;

namespace Core.Entities;

[JsonConverter(typeof(HashKeyJsonConverter))]
public readonly struct HashKey : IEquatable<HashKey>
{
    private Vector256<byte> Hash { get; }

    public HashKey(ReadOnlySpan<byte> hash)
    {
        Hash = Vector256.Create(hash);
    }
    
    public HashKey(string hash)
    {
        Hash = Vector256.Create(Convert.FromHexString(hash));
    }

    public override string ToString()
    {
        Span<byte> hash = stackalloc byte[Vector256<byte>.Count];
        Hash.CopyTo(hash);
        return Convert.ToHexString(hash);
    }

    public bool Equals(HashKey other)
    {
        return Hash.Equals(other.Hash);
    }

    public override bool Equals(object? obj)
    {
        return obj is HashKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Hash.GetHashCode();
    }

    public static bool operator ==(HashKey left, HashKey right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(HashKey left, HashKey right)
    {
        return !left.Equals(right);
    }
}