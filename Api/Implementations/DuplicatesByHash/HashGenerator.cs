using Core.Interfaces;
using Microsoft.Win32.SafeHandles;
using System.Runtime.CompilerServices;
using Blake3;
using CommunityToolkit.HighPerformance.Buffers;

namespace Api.Implementations.DuplicatesByHash;

public class HashGenerator : IHashGenerator
{
    private const int BufferSize = 1_048_576;
    private const int HashSize = 32;
    

    [SkipLocalsInit]
    public ValueTask<string?> GenerateHashAsync(SafeFileHandle fileHandle, long bytesToHash,
        CancellationToken cancellationToken)
    {
        if (bytesToHash == 0)
            return ValueTask.FromResult<string?>(null);

        Span<byte> buffer = stackalloc byte[BufferSize];
        using var hasher = Hasher.New();

        var bytesHashed = 0L;

        while (bytesHashed < bytesToHash)
        {
            var remainingToHash = bytesToHash - bytesHashed;
            if (remainingToHash > buffer.Length)
            {
                bytesHashed += RandomAccess.Read(fileHandle, buffer, bytesHashed);
                hasher.UpdateWithJoin(buffer);
            }
            else
            {
                bytesHashed += RandomAccess.Read(fileHandle, buffer[..(int)remainingToHash], bytesHashed);
                if (remainingToHash > 131072)
                    hasher.UpdateWithJoin(buffer[..(int)remainingToHash]);
                else
                    hasher.Update(buffer[..(int)remainingToHash]);
            }
        }

        // Reuse the buffer already allocated on the stack instead of allocating a new one
        hasher.Finalize(buffer[..HashSize]);
        Span<char> charBuffer = stackalloc char[HashSize * 2];
        Convert.TryToHexStringLower(buffer[..HashSize], charBuffer, out _);
        return ValueTask.FromResult<string?>(StringPool.Shared.GetOrAdd(charBuffer));
    }
}