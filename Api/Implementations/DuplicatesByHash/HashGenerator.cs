using System.Runtime.CompilerServices;
using Core.Interfaces;
using Microsoft.Win32.SafeHandles;
using Blake3;
using CommunityToolkit.HighPerformance.Buffers;
using Core.Interfaces.Common;

namespace Api.Implementations.DuplicatesByHash;

public class HashGenerator : IHashGenerator
{
    private const int MinSizeForMultiThreading = 131072;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async ValueTask<byte[]?> GenerateHash(SafeFileHandle fileHandle, long bytesToHash,
        CancellationToken cancellationToken)
    {
        if (bytesToHash == 0)
            return null;

        var buffer = MemoryOwner<byte>.Allocate(bytesToHash >= MinSizeForMultiThreading
            ? MinSizeForMultiThreading
            : (int)bytesToHash);

        using var hasher = Hasher.New();

        var bytesHashed = 0L;

        while (bytesHashed < bytesToHash)
        {
            var remainingToHash = bytesToHash - bytesHashed;
            if (remainingToHash < buffer.Length)
                buffer = buffer[..(int)remainingToHash];
            
            var bytesRead = await RandomAccess.ReadAsync(fileHandle, buffer.Memory, bytesHashed,cancellationToken);
            
            if (bytesRead == MinSizeForMultiThreading)
                hasher.UpdateWithJoin(buffer.Span);
            else
                hasher.Update(buffer.Span);
            
            bytesHashed += bytesRead;
        }

        buffer.Dispose();
        return hasher.Finalize().AsSpan().ToArray();
    }
}