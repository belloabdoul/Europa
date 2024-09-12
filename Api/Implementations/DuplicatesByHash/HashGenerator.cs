using Core.Interfaces;
using Microsoft.Win32.SafeHandles;
using System.Runtime.CompilerServices;
using Blake3;
using CommunityToolkit.HighPerformance.Buffers;

namespace Api.Implementations.DuplicatesByHash;

public class HashGenerator : IHashGenerator
{
    private const int BufferSize = 1_048_576;

    [SkipLocalsInit]
    public string? GenerateHash(SafeFileHandle fileHandle, long bytesToHash, CancellationToken cancellationToken)
    {
        if (bytesToHash == 0)
            return null;

        Span<byte> buffer = stackalloc byte[BufferSize];

        using var hasher = Hasher.New();

        var bytesHashed = 0L;

        while (bytesHashed < bytesToHash)
        {
            var remainingToHash = bytesToHash - bytesHashed;
            if (remainingToHash > BufferSize)
                remainingToHash = BufferSize;

            RandomAccess.Read(fileHandle, buffer[..(int)remainingToHash], bytesHashed);

            switch (remainingToHash)
            {
                case >= BufferSize:
                    hasher.UpdateWithJoin(buffer);
                    break;
                case >= 131072:
                    hasher.UpdateWithJoin(buffer[..(int)remainingToHash]);
                    break;
                default:
                    hasher.Update(buffer[..(int)remainingToHash]);
                    break;
            }

            bytesHashed += remainingToHash;
        }

        var hash = hasher.Finalize().AsSpan();
        Span<char> tempHash = stackalloc char[hash.Length * 2];
        Convert.TryToHexStringLower(hash, tempHash, out _);
        return StringPool.Shared.GetOrAdd(tempHash);
    }
}