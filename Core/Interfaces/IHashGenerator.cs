using Microsoft.Win32.SafeHandles;

namespace Core.Interfaces;

public interface IHashGenerator
{
    ValueTask<string?> GenerateHashAsync(SafeFileHandle fileHandle, long bytesToHash, CancellationToken cancellationToken);
}