using Blake3;
using CommunityToolkit.HighPerformance.Buffers;
using Core.Interfaces.DuplicatesByHash;
using Microsoft.Win32.SafeHandles;

namespace API.Implementations.DuplicatesByHash;

public class HashGenerator : IHashGenerator
{
    public async Task<Hash?> GenerateHashAsync(SafeFileHandle fileHandle, long bytesToHash,
        CancellationToken cancellationToken)
    {
        if (RandomAccess.GetLength(fileHandle) == 0)
            return null;

        const int bufferSize = 1048576;
        
        using var hasher = Hasher.New();
        using var bytesRead = MemoryOwner<byte>.Allocate(bufferSize);
        var bytesHashed = 0;
        
        while (bytesHashed < bytesToHash)
        {
            var remainingToHash = bytesToHash - bytesHashed;
            if (remainingToHash > bufferSize)
            {
                // if the file is bigger than 1MiB, make use of blake3's multithreading
                bytesHashed += await RandomAccess.ReadAsync(fileHandle, bytesRead.Memory[..bufferSize], bytesHashed,
                    cancellationToken: cancellationToken);
                hasher.UpdateWithJoin(bytesRead.Span[..bufferSize]);
            }
            else
            {
                // if the file is smaller than 1MB, make use of blake3's multithreading only if the file is bigger than
                // 128KiB
                bytesHashed += await RandomAccess.ReadAsync(fileHandle, bytesRead.Memory[.. (int)remainingToHash],
                    bytesHashed, cancellationToken: cancellationToken);
                if(remainingToHash > 131072)
                    hasher.UpdateWithJoin(bytesRead.Memory.Span[.. (int)remainingToHash]);
                else
                    hasher.Update(bytesRead.Span[.. (int)remainingToHash]);
            }
        }
        
        return hasher.Finalize();
    }
}