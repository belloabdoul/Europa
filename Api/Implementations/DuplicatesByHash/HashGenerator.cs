using System.Buffers;
using Api.Implementations.Commons;
using Blake3;
using CommunityToolkit.HighPerformance.Buffers;
using Core.Interfaces.Commons;
using Microsoft.Win32.SafeHandles;

namespace Api.Implementations.DuplicatesByHash;

public sealed class HashGenerator : IHashGenerator
{
    private const int MinBufferSizeForMultiThreading = 131_072;

    private readonly ArrayPool<byte> _pool =
        ArrayPool<byte>.Create(MinBufferSizeForMultiThreading, Environment.ProcessorCount);
    
    public ValueTask<byte[]> GenerateHashAsync(string hypotheticalDuplicate,
        Func<long, long> getFileLengthToHashFunction, CancellationToken cancellationToken = default)
    {
        var fileHandle = FileReader.GetFileHandle(hypotheticalDuplicate, true, true);
        var lengthToHash = getFileLengthToHashFunction(RandomAccess.GetLength(fileHandle));

        return lengthToHash == 0
            ? ValueTask.FromResult(Array.Empty<byte>())
            : GenerateHashAsyncCore(fileHandle, lengthToHash, _pool, cancellationToken);
    }

    private static async ValueTask<byte[]> GenerateHashAsyncCore(SafeFileHandle fileHandle, long lengthToHash, ArrayPool<byte> arrayPool,
        CancellationToken cancellationToken)
    {
        var rentedBuffer = MemoryOwner<byte>.Allocate(MinBufferSizeForMultiThreading, arrayPool);
        try
        {
            using var hasher = Hasher.New();
            using var handle = fileHandle;

            var bytesRead = 0L;
            do
            {
                rentedBuffer = rentedBuffer[..Convert.ToInt32(long.Min(lengthToHash - bytesRead,
                    MinBufferSizeForMultiThreading))];
                bytesRead +=
                    await RandomAccess.ReadAsync(handle, rentedBuffer.Memory, bytesRead, cancellationToken);
                hasher.Update(rentedBuffer.Span);
            } while (bytesRead < lengthToHash);


            var hash = GC.AllocateUninitializedArray<byte>(32);
            hasher.Finalize(hash);
            return hash;
        }
        finally
        {
            rentedBuffer.Dispose();
        }
    }
}