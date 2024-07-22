using Blake3;
using Microsoft.Win32.SafeHandles;

namespace Core.Interfaces.DuplicatesByHash;

public interface IHashGenerator
{
    Task<Hash?> GenerateHashAsync(SafeFileHandle fileHandle, long bytesToHash, CancellationToken cancellationToken);
}