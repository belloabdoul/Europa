using Blake3;
using Microsoft.Win32.SafeHandles;

namespace Core.Interfaces.DuplicatesByHash;

public interface IHashGenerator
{
    Task<byte[]> GenerateHashAsync(SafeFileHandle fileHandle, long bytesToHash, CancellationToken cancellationToken);
}