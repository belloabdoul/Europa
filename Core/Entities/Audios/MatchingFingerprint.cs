using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Core.Entities.Audios;

public struct MatchingFingerprint: IEquatable<MatchingFingerprint>
{
    private static readonly UInt128 USeed = new(BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)),
        BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8)));
    public byte[] FileHash { get; set; }
    public double StartAt { get; set; }


    public bool Equals(MatchingFingerprint other)
    {
        return FileHash.AsSpan().SequenceEqual(other.FileHash) && StartAt.CompareTo(other.StartAt) == 0;
    }

    public override bool Equals(object? obj)
    {
        return obj is MatchingFingerprint other && Equals(other);
    }

    [SkipLocalsInit]
    public override int GetHashCode()
    {
        Span<byte> hash = stackalloc byte[FileHash.Length + sizeof(double)];
        FileHash.CopyTo(hash);
        Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(hash[FileHash.Length..]), StartAt);
        return GxHash.GxHash.Hash32(hash, USeed);
    }
}