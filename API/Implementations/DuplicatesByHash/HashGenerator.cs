using Blake3;
using CommunityToolkit.HighPerformance.Buffers;
using Core.Interfaces;
using Microsoft.Win32.SafeHandles;

namespace API.Implementations.DuplicatesByHash;

public class HashGenerator : IHashGenerator
{
    public async ValueTask<string?> GenerateHashAsync(SafeFileHandle fileHandle, long bytesToHash, Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        if (bytesToHash == 0)
            return null;

        // var buffers = FileReader.GetPageAlignedMemoryList(buffer);
        using var hasher = Hasher.New();
        var bytesHashed = 0L;

        while (bytesHashed < bytesToHash)
        {
            var remainingToHash = bytesToHash - bytesHashed;
            if (remainingToHash >= buffer.Length)
                remainingToHash = buffer.Length;

            bytesHashed +=
                // await RandomAccess.ReadAsync(fileHandle, new ArraySegment<Memory<byte>>(buffers, 0, (int)Math.Round(
                //         decimal.Divide(remainingToHash, Environment.SystemPageSize),
                //         MidpointRounding.ToPositiveInfinity)), bytesHashed,
                //     cancellationToken);
                await RandomAccess.ReadAsync(fileHandle, buffer[..(int)remainingToHash], bytesHashed,
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