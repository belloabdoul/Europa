using Blake3;
using CommunityToolkit.HighPerformance.Buffers;
using Core.Interfaces.Commons;
using Microsoft.Win32.SafeHandles;

namespace Api.Implementations.DuplicatesByHash;

public class HashGenerator : IHashGenerator
{
    private const int MinSizeForMultiThreading = 131072;

    public async ValueTask<byte[]> GenerateHash(SafeFileHandle fileHandle, long bytesToHash,
        CancellationToken cancellationToken)
    {
        if (bytesToHash == 0)
            return [];

        using var buffer = MemoryOwner<byte>.Allocate(bytesToHash >= MinSizeForMultiThreading
            ? MinSizeForMultiThreading
            : (int)bytesToHash);

        using var hasher = Hasher.New();

        var bytesHashed = 0L;

        while (bytesHashed < bytesToHash)
        {
            var remainingToHash = bytesToHash - bytesHashed;

            var bytesRead = await RandomAccess.ReadAsync(fileHandle,
                remainingToHash >= buffer.Length ? buffer.Memory : buffer.Memory[..(int)remainingToHash], bytesHashed,
                cancellationToken);

            if (bytesRead == MinSizeForMultiThreading)
                hasher.UpdateWithJoin(buffer.Span[..bytesRead]);
            else
                hasher.Update(buffer.Span[..bytesRead]);

            bytesHashed += bytesRead;
        }

        return hasher.Finalize().AsSpan().ToArray();
    }
}