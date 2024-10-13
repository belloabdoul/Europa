using System.Buffers;
using Core.Interfaces;
using Microsoft.Win32.SafeHandles;
using Blake3;
using DotNext.Buffers.Text;
using U8;
using U8.InteropServices;

namespace Api.Implementations.DuplicatesByHash;

public class HashGenerator : IHashGenerator
{
    private const int BufferSize = 1_048_576;

    public async ValueTask<U8String?> GenerateHash(SafeFileHandle fileHandle, long bytesToHash,
        CancellationToken cancellationToken)
    {
        if (bytesToHash == 0)
            return null;

        using var buffer = new DotNext.Buffers.MemoryOwner<byte>(ArrayPool<byte>.Shared, 1_048_576);

        using var hasher = Hasher.New();

        var bytesHashed = 0L;

        while (bytesHashed < bytesToHash)
        {
            var remainingToHash = (int)(bytesToHash - bytesHashed);
            var bytesRead = await RandomAccess.ReadAsync(fileHandle,
                remainingToHash > BufferSize ? buffer.Memory : buffer.Memory[..remainingToHash], bytesHashed,
                cancellationToken);
            hasher.UpdateWithJoin(buffer.Span[..bytesRead]);
            bytesHashed += bytesRead;
        }

        return U8Marshal.CreateUnsafe(Hex.EncodeToUtf8(hasher.Finalize().AsSpan(), true));
    }
}