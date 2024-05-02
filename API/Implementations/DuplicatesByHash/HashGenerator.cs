using Blake3;
using Core.Interfaces.DuplicatesByHash;
using DotNext.IO.MemoryMappedFiles;

namespace API.Implementations.DuplicatesByHash
{
    public class HashGenerator : IHashGenerator
    {
        public string GenerateHash(FileStream fileStream, long lengthToHash)
        {
            using var hasher = Hasher.New();

            // Map the file to memory by allocating one segment of 81920 bytes which will be refilled to hash the file partially
            using var fileSegments = fileStream.Length < 81920 ? new ReadOnlySequenceAccessor(fileStream, Convert.ToInt32(fileStream.Length)) : new ReadOnlySequenceAccessor(fileStream, 81920);
            foreach (var segment in fileSegments.Sequence)
            {
                hasher.Update(segment.Span);
            }
            
            return hasher.Finalize().ToString();
        }
    }
}
