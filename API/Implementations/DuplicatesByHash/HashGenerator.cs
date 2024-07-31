using Blake3;
using CommunityToolkit.HighPerformance.Buffers;
using Core.Interfaces;
using DotNext.Buffers;
using MemoryOwner = DotNext.Buffers.MemoryOwner<byte>;

namespace API.Implementations.DuplicatesByHash;

public class HashGenerator : IHashGenerator
{
    public async Task<string?> GenerateHashAsync(FileStream fileHandle, long bytesToHash,
        CancellationToken cancellationToken)
    {
        if (fileHandle.Length == 0)
            return null;

        const int bufferSize = 1048576;

        using var buffer = new MemoryOwner(UnmanagedMemoryPool<byte>.Shared, bufferSize);
        await using var blake3Stream = new Blake3Stream(fileHandle);
        var bytesHashed = 0;
        
        while (bytesHashed < bytesToHash)
        {
            var remainingToHash = bytesToHash - bytesHashed;
            if (remainingToHash > bufferSize)
                bytesHashed += await blake3Stream.ReadAsync(buffer.Memory, cancellationToken: cancellationToken);
            else
                bytesHashed += await blake3Stream.ReadAsync(buffer.Memory[..(int)remainingToHash], cancellationToken: cancellationToken);
        }
        
        return StringPool.Shared.GetOrAdd(blake3Stream.ComputeHash().ToString());
    }
}