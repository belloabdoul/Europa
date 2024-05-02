using Blake3;
using Core.Interfaces.DuplicatesByHash;
using DotNext.IO.MemoryMappedFiles;
using SQLitePCL;

namespace API.Implementations.DuplicatesByHash
{
    public class HashGenerator : IHashGenerator
    {
        public string GenerateHash(FileStream fileStream, long lengthToHash)
        {
            using var hasher = Hasher.New();

            if (fileStream.Length == 0)
                return string.Empty;

            // Map the file to memory by allocating one segment of 81920 bytes which will be refilled to hash the file partially
            using var fileSegments = fileStream.Length < 81920
                ? new ReadOnlySequenceAccessor(fileStream, Convert.ToInt32(fileStream.Length))
                : new ReadOnlySequenceAccessor(fileStream, 81920);

            var enumerator = fileSegments.Sequence.GetEnumerator();
            var lengthHashed = 0L;
            while (lengthHashed < lengthToHash && enumerator.MoveNext())
            {
                var current = enumerator.Current.Span;
                var remainingToHash = lengthToHash - lengthHashed;
                if (remainingToHash > current.Length)
                {
                    hasher.Update(current);
                    lengthHashed += current.Length;
                }
                else
                {
                    hasher.Update(current[..(int)remainingToHash]);
                    lengthHashed = lengthToHash;
                }
            }

            return hasher.Finalize().ToString();
        }
    }
}