using Microsoft.Win32.SafeHandles;
using U8;

namespace Core.Interfaces;

public interface IHashGenerator
{
    ValueTask<U8String?> GenerateHash(SafeFileHandle fileHandle, long bytesToHash, CancellationToken cancellationToken);
}