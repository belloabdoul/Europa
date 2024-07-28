using Blake3;
using CommunityToolkit.HighPerformance.Buffers;
using Core.Interfaces.DuplicatesByHash;
using DotNext.Buffers;
using Microsoft.Win32.SafeHandles;

namespace API.Implementations.DuplicatesByHash;

public class HashGenerator : IHashGenerator
{
    public async Task<string?> GenerateHashAsync(SafeFileHandle fileHandle, long bytesToHash,
        CancellationToken cancellationToken)
    {
        if (RandomAccess.GetLength(fileHandle) == 0)
            return null;

        const int bufferSize = 1048576;
        
        using var hasher = Hasher.New();
        using var buffer = new DotNext.Buffers.MemoryOwner<byte>(UnmanagedMemoryPool<byte>.Shared, bufferSize);
        var bytesHashed = 0;
        
        while (bytesHashed < bytesToHash)
        {
            var remainingToHash = bytesToHash - bytesHashed;
            if (remainingToHash > bufferSize)
            {
                // if the file is bigger than 1MiB, make use of blake3's multithreading
                bytesHashed += await RandomAccess.ReadAsync(fileHandle, buffer.Memory[..bufferSize], bytesHashed,
                    cancellationToken: cancellationToken);
                hasher.UpdateWithJoin(buffer.Span[..bufferSize]);
            }
            else
            {
                // if the file is smaller than 1MB, make use of blake3's multithreading only if the file is bigger than
                // 128KiB
                bytesHashed += await RandomAccess.ReadAsync(fileHandle, buffer.Memory[.. (int)remainingToHash],
                    bytesHashed, cancellationToken: cancellationToken);
                if(remainingToHash > 131072)
                    hasher.UpdateWithJoin(buffer.Span[.. (int)remainingToHash]);
                else
                    hasher.Update(buffer.Span[.. (int)remainingToHash]);
            }
        }
        
        return StringPool.Shared.GetOrAdd(hasher.Finalize().ToString());
    }
}