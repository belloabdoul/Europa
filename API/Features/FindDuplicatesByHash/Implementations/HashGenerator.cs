using API.Features.FindDuplicatesByHash.Interfaces;
using Blake3;
using NetTopologySuite.Algorithm;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO.MemoryMappedFiles;

namespace API.Features.FindDuplicatesByHash.Implementations
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
