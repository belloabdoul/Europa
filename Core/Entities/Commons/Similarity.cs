using System.IO.Hashing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Core.Entities.Commons;

public struct Similarity : IEquatable<Similarity>
{
    public byte[] OriginalId { get; set; }

    public byte[] DuplicateId { get; set; }

    public decimal Score { get; set; }

    private static readonly UInt128 USeed = new(BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)),
        BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)));

    [SkipLocalsInit]
    public override int GetHashCode()
    {
        Span<byte> hash = stackalloc byte[OriginalId.Length + DuplicateId.Length + sizeof(decimal)];
        OriginalId.CopyTo(hash);
        DuplicateId.CopyTo(hash[OriginalId.Length..]);
        Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(hash[(OriginalId.Length + DuplicateId.Length)..]), Score);
        return GxHash.GxHash.Hash32(hash, USeed);
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