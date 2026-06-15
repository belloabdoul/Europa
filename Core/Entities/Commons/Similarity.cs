using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Core.Entities.Commons;

public readonly struct Similarity : IEquatable<Similarity>
{
    public long OriginalId { get; init; }

    public long DuplicateId { get; init; }

    public decimal Score { get; init; }

    private static readonly UInt128 USeed = new(BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)),
        BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)));

    [SkipLocalsInit]
    public override int GetHashCode()
    {
        Span<byte> hash = stackalloc byte[2 * sizeof(long) + sizeof(decimal)];
        Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(hash), OriginalId);
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref MemoryMarshal.GetReference(hash), sizeof(long)), DuplicateId);
        Unsafe.WriteUnaligned(ref Unsafe.Add(ref MemoryMarshal.GetReference(hash), 2 * sizeof(long)), Score);
        // HashCode.Combine(OriginalId, DuplicateId, Score);
        return GxHash.GxHash.Hash32(hash, USeed);
    }

    public override bool Equals(object? obj)
    {
        return obj is Similarity other && Equals(other);
    }

    public bool Equals(Similarity other)
    {
        return OriginalId == other.OriginalId && DuplicateId == other.DuplicateId && Score == other.Score;
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