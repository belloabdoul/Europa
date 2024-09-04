using Blake3;
using Core.Interfaces;
using Microsoft.Win32.SafeHandles;
using System.Runtime.CompilerServices;

namespace Api.Implementations.DuplicatesByHash;

public class HashGenerator : IHashGenerator
{
    private const int BufferSize = 1_048_576;

    [SkipLocalsInit]
    public Hash? GenerateHash(SafeFileHandle fileHandle, long bytesToHash, CancellationToken cancellationToken)
    {
        if (bytesToHash == 0)
            return null;

        Span<byte> buffer = stackalloc byte[BufferSize];

        using var hasher = Hasher.New();
        var bytesHashed = 0L;

        while (bytesHashed < bytesToHash)
        {
            var remainingToHash = bytesToHash - bytesHashed;
            if (remainingToHash >= buffer.Length)
            {
                bytesHashed += RandomAccess.Read(fileHandle, buffer, bytesHashed);
                hasher.UpdateWithJoin(buffer);
            }
            else
            {
                bytesHashed += RandomAccess.Read(fileHandle, buffer[..(int)remainingToHash], bytesHashed);
                if (remainingToHash >= 131072)
                    hasher.UpdateWithJoin(buffer[..(int)remainingToHash]);
                else
                    hasher.Update(buffer[..(int)remainingToHash]);
            }
        }

        return hasher.Finalize();
    }
}