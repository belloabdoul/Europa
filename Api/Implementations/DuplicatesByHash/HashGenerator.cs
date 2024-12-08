using Blake3;
using Core.Interfaces.Commons;
using DotNext.IO;
using DotNext.IO.MemoryMappedFiles;

namespace Api.Implementations.DuplicatesByHash;

public class HashGenerator : IHashGenerator
{
    public async ValueTask<byte[]> GenerateHash(ReadOnlySequenceAccessor? readOnlySequenceAccessor,
        CancellationToken cancellationToken)
    {
        if (readOnlySequenceAccessor == null)
            return []; 
        
        await using var stream = readOnlySequenceAccessor.Sequence.AsStream();

        using var blake3 = new Blake3HashAlgorithm();
        return await blake3.ComputeHashAsync(stream, cancellationToken);
    }
}