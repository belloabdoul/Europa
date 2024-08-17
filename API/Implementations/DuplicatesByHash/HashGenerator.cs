using API.Implementations.Common;
using Blake3;
using CommunityToolkit.HighPerformance.Buffers;
using Core.Interfaces;
using DotNext.Buffers;
using Microsoft.Win32.SafeHandles;
using Pipelines.Sockets.Unofficial;

namespace API.Implementations.DuplicatesByHash;

public class HashGenerator : IHashGenerator
{
    private const int BufferSize = 1_048_576;

    // public async ValueTask<string?> GenerateHashAsync(SafeFileHandle fileHandle, long bytesToHash,
    //     IntPtr bufferPointer, int bufferSize, CancellationToken cancellationToken)
    // public async ValueTask<string?> GenerateHashAsync(SafeFileHandle fileHandle, long bytesToHash,
    //     CancellationToken cancellationToken)
    public async ValueTask<string?> GenerateHashAsync(SafeFileHandle fileHandle, long bytesToHash, Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        if (bytesToHash == 0)
            return null;

        // using var buffer = new UnmanagedMemoryManager<byte>(bufferPointer, bufferSize);
        var pageAlignedMemoryList = FileReader.GetPageAlignedMemoryList(buffer);
        // using var buffer = UnmanagedMemoryPool<byte>.Shared.Rent(BufferSize);
        using var hasher = Hasher.New();
        var bytesHashed = 0L;

        while (bytesHashed < bytesToHash)
        {
            var remainingToHash = bytesToHash - bytesHashed;
            if (remainingToHash >= buffer.Length)
                remainingToHash = buffer.Length;

            // bytesHashed += await RandomAccess.ReadAsync(fileHandle, buffer.Memory[..(int)remainingToHash],
            //     bytesHashed, cancellationToken);

            bytesHashed +=
                await RandomAccess.ReadAsync(fileHandle,
                    new ArraySegment<Memory<byte>>(pageAlignedMemoryList, 0,
                        (int)Math.Round(decimal.Divide(remainingToHash, Environment.SystemPageSize),
                            MidpointRounding.ToPositiveInfinity)), bytesHashed,
                    cancellationToken);

            if (remainingToHash == buffer.Length)
                hasher.UpdateWithJoin(buffer.Span);
            else if (remainingToHash >= 131072)
                hasher.UpdateWithJoin(buffer.Span[..(int)remainingToHash]);
            else
                hasher.Update(buffer.Span[..(int)remainingToHash]);
        }

        return StringPool.Shared.GetOrAdd(hasher.Finalize().ToString());
    }
}