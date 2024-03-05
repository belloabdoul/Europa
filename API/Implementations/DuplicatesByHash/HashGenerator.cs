using API.Interfaces.DuplicatesByHash;
using Blake3;

namespace API.Implementations.DuplicatesByHash
{
    public class HashGenerator : IHashGenerator
    {
        public string GenerateHash(string path)
        {
            using var hasher = Hasher.New();

            using var stream = File.OpenRead(path);

            using var blake3Stream = new Blake3Stream(stream);

            blake3Stream.Read(new byte[stream.Length]);

            return blake3Stream.ComputeHash().ToString();
        }
    }
}
