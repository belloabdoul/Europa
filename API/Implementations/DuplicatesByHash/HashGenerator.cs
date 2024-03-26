using Blake3;
using Core.Interfaces.DuplicatesByHash;

namespace API.Implementations.DuplicatesByHash
{
    public class HashGenerator : IHashGenerator
    {
        public string GenerateHash(FileStream fileStream)
        {
            using var hasher = Hasher.New();

            using var blake3Stream = new Blake3Stream(fileStream);

            blake3Stream.Read(new byte[fileStream.Length]);

            return blake3Stream.ComputeHash().ToString();
        }
    }
}
