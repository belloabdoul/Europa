using System.Buffers;
using Blake3;
using Core.Entities;
using Core.Interfaces.DuplicatesByHash;
using Microsoft.Win32.SafeHandles;
using NoAlloq;

namespace API.Implementations.DuplicatesByHash;

public class HashGenerator : IHashGenerator
{
    public async Task<byte[]> GenerateHashAsync(SafeFileHandle fileHandle, long bytesToHash,
        CancellationToken cancellationToken)
    {
        if (RandomAccess.GetLength(fileHandle) == 0)
            return [];

        const int bufferSize = 16384;
        
        using var hasher = Hasher.New();
        using var bytesRead = MemoryPool<byte>.Shared.Rent(bufferSize);
        var bytesHashed = 0;
        
        while (bytesHashed < bytesToHash)
        {
            var remainingToHash = bytesToHash - bytesHashed;
            if (remainingToHash > bufferSize)
            {
                bytesHashed += await RandomAccess.ReadAsync(fileHandle, bytesRead.Memory[..bufferSize], bytesHashed,
                    cancellationToken: cancellationToken);
                hasher.Update(bytesRead.Memory.Span[..bufferSize]);
            }
            else
            {
                bytesHashed += await RandomAccess.ReadAsync(fileHandle, bytesRead.Memory[.. (int)remainingToHash],
                    bytesHashed, cancellationToken: cancellationToken);
                hasher.Update(bytesRead.Memory.Span[.. (int)remainingToHash]);
            }
        }
        
        return hasher.Finalize().AsSpan().Select(byteValue => Utilities.ByteToByte[byteValue]).ToArray();
    }
}