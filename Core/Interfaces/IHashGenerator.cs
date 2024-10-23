using Microsoft.Win32.SafeHandles;

namespace Core.Interfaces;

public interface IHashGenerator
{
    ValueTask<byte[]?> GenerateHash(SafeFileHandle fileHandle, long bytesToHash, CancellationToken cancellationToken);
}