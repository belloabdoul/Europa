using System.Runtime.Intrinsics;

namespace Core.Entities;

public readonly struct HashKey : IEquatable<HashKey>
{
    private Vector256<byte> Hash { get; }

    public HashKey(ReadOnlySpan<byte> hash)
    {
        Hash = Vector256.Create(hash);
    }

    public override string ToString()
    {
        Span<byte> hash = stackalloc byte[Vector256<byte>.Count];
        Hash.CopyTo(hash);
        return Convert.ToHexStringLower(hash);
    }

    private static readonly char[] Hex =
    [
        '0',
        '1',
        '2',
        '3',
        '4',
        '5',
        '6',
        '7',
        '8',
        '9',
        'a',
        'b',
        'c',
        'd',
        'e',
        'f'
    ];

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