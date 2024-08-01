using Blake3;
using CommunityToolkit.HighPerformance.Buffers;
using Core.Interfaces;
using DotNext.Buffers;
using Microsoft.Win32.SafeHandles;
using MemoryOwner = DotNext.Buffers.MemoryOwner<byte>;

namespace API.Implementations.DuplicatesByHash;

public class HashGenerator : IHashGenerator
{
    public async ValueTask<string?> GenerateHashAsync(SafeFileHandle fileHandle, long bytesToHash,
        CancellationToken cancellationToken)
    {
        if (bytesToHash == 0)
            return null;

        const int bufferSize = 1048576;

        using var buffer = new MemoryOwner(UnmanagedMemoryPool<byte>.Shared, bufferSize);
        using var hasher = Hasher.New();
        var bytesHashed = 0;
        
        while (bytesHashed < bytesToHash)
        {
            var remainingToHash = bytesToHash - bytesHashed;
            if (remainingToHash > bufferSize)
            {
                bytesHashed += await RandomAccess.ReadAsync(fileHandle, buffer.Memory, bytesHashed,
                    cancellationToken: cancellationToken);
                hasher.Update(buffer.Span);
            }
            else
            {
                bytesHashed += await RandomAccess.ReadAsync(fileHandle, buffer.Memory[..(int)remainingToHash],
                    bytesHashed, cancellationToken: cancellationToken);
                hasher.Update(buffer.Span[..(int)remainingToHash]);
            }
        }
        
        return StringPool.Shared.GetOrAdd(hasher.Finalize().ToString());
    }
}