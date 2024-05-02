using Core.Interfaces.Common;

namespace API.Implementations.Common
{
    public class FileReader : IFileReader
    {
        public FileStream GetFileStream(string path)
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16384);
        }
    }
}
