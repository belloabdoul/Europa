using Microsoft.Win32.SafeHandles;

namespace Core.Interfaces.Commons;

public interface IHashGenerator
{
    ValueTask<byte[]?> GenerateHash(SafeFileHandle fileHandle, long bytesToHash, CancellationToken cancellationToken);
}