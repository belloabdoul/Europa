using Blake3;
using Microsoft.Win32.SafeHandles;

namespace Core.Interfaces;

public interface IHashGenerator
{
    string? GenerateHash(SafeFileHandle fileHandle, long bytesToHash, CancellationToken cancellationToken);
}