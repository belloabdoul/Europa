using Blake3;
using System.Buffers;

namespace API.Features.FindDuplicatesByHash
{
    public class HashGenerator : IHashGenerator
    {
        public string GenerateHash(string path)
        {
            using var stream = File.OpenRead(path);

            using var hasher = Hasher.New();
            var buffer = ArrayPool<byte>.Shared.Rent(4096);

            int bytesRead;
            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                hasher.UpdateWithJoin(buffer.AsSpan(0, bytesRead));
            }

            ArrayPool<byte>.Shared.Return(buffer);

            return hasher.Finalize().ToString();
        }
    }
}
