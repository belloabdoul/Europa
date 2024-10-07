using Core.Interfaces;
using Microsoft.Win32.SafeHandles;
using System.Runtime.CompilerServices;
using Blake3;
using DotNext.Buffers.Text;
using U8;
using U8.InteropServices;

namespace Api.Implementations.DuplicatesByHash;

public class HashGenerator : IHashGenerator
{
    private const int BufferSize = 1_048_576;

    [SkipLocalsInit]
    public ValueTask<U8String?> GenerateHash(SafeFileHandle fileHandle, long bytesToHash,
        CancellationToken cancellationToken)
    {
        if (bytesToHash == 0)
            return ValueTask.FromResult<U8String?>(null);

        Span<byte> buffer = stackalloc byte[BufferSize];
        
        using var hasher = Hasher.New();

        var bytesHashed = 0L;

        while (bytesHashed < bytesToHash)
        {
            var remainingToHash = (int)(bytesToHash - bytesHashed);
            var bytesRead = RandomAccess.Read(fileHandle,
                remainingToHash > BufferSize ? buffer : buffer[..remainingToHash], bytesHashed);
            hasher.UpdateWithJoin(buffer[..bytesRead]);
            bytesHashed += bytesRead;
        }
        
        return ValueTask.FromResult<U8String?>(
            U8Marshal.CreateUnsafe(Hex.EncodeToUtf8(hasher.Finalize().AsSpan(), true)));
    }
}