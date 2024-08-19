using Core.Entities;
using Microsoft.Win32.SafeHandles;

namespace Core.Interfaces;

public interface IHashGenerator
{
    HashKey? GenerateHash(SafeFileHandle fileHandle, long bytesToHash, CancellationToken cancellationToken);

    // ValueTask<string?> GenerateHashAsync(SafeFileHandle fileHandle, long bytesToHash, IntPtr bufferPointer,
    //     int bufferSize, CancellationToken cancellationToken);

    // ValueTask<string?> GenerateHashAsync(SafeFileHandle fileHandle, long bytesToHash,
    //     CancellationToken cancellationToken);
}