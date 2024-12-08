using DotNext.IO.MemoryMappedFiles;

namespace Core.Interfaces.Commons;

public interface IHashGenerator
{
    ValueTask<byte[]> GenerateHash(ReadOnlySequenceAccessor? fileHandle, CancellationToken cancellationToken);
}