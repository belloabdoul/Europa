using Blake3;
using CommunityToolkit.HighPerformance.Buffers;
using Core.Interfaces;
using DotNext.Buffers;
using Microsoft.Win32.SafeHandles;

namespace API.Implementations.DuplicatesByHash;

public class HashGenerator : IHashGenerator
{
    public async ValueTask<string?> GenerateHashAsync(SafeFileHandle fileHandle, long bytesToHash,
        CancellationToken cancellationToken)
    {
        if (bytesToHash == 0)
            return null;

        const int bufferSize = 1048576;

        using var buffer = UnmanagedMemoryPool<byte>.Shared.Rent(bufferSize);
        using var hasher = Hasher.New();
        var bytesHashed = 0;

        while (bytesHashed < bytesToHash)
        {
            var remainingToHash = bytesToHash - bytesHashed;
            if (remainingToHash > bufferSize)
                remainingToHash = bufferSize;

            bytesHashed += await RandomAccess.ReadAsync(fileHandle, buffer.Memory[..(int)remainingToHash],
                fileOffset: bytesHashed, cancellationToken: cancellationToken);

            if (remainingToHash >= 131072)
                hasher.UpdateWithJoin(buffer.Memory[..(int)remainingToHash].Span);
            else
                hasher.Update(buffer.Memory[..(int)remainingToHash].Span);
        }

        return StringPool.Shared.GetOrAdd(hasher.Finalize().ToString());
    }
}