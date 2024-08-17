using Blake3;
using CommunityToolkit.HighPerformance.Buffers;
using Core.Interfaces;
using DotNext.Buffers;
using Microsoft.Win32.SafeHandles;

namespace API.Implementations.DuplicatesByHash;

public class HashGenerator : IHashGenerator
{
    private const int BufferSize = 1048576;

    // public async ValueTask<string?> GenerateHashAsync(SafeFileHandle fileHandle, long bytesToHash,
    //     IntPtr bufferPointer, int bufferSize, CancellationToken cancellationToken)
    public async ValueTask<string?> GenerateHashAsync(SafeFileHandle fileHandle, long bytesToHash,
        CancellationToken cancellationToken)
    {
        if (bytesToHash == 0)
            return null;

        // using var buffer = new UnmanagedMemoryManager<byte>(bufferPointer, bufferSize);
        using var buffer = UnmanagedMemoryPool<byte>.Shared.Rent(BufferSize);
        using var hasher = Hasher.New();
        var bytesHashed = 0;

        while (bytesHashed < bytesToHash)
        {
            var remainingToHash = bytesToHash - bytesHashed;
            if (remainingToHash > BufferSize)
                remainingToHash = BufferSize;

            bytesHashed += await RandomAccess.ReadAsync(fileHandle, buffer.Memory[..(int)remainingToHash],
                bytesHashed, cancellationToken);

            if (remainingToHash >= 131072)
                hasher.UpdateWithJoin(buffer.Memory.Span[..(int)remainingToHash]);
            else
                hasher.Update(buffer.Memory.Span[..(int)remainingToHash]);
        }

        return StringPool.Shared.GetOrAdd(hasher.Finalize().ToString());
    }
}