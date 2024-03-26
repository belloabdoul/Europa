using Core.Interfaces.Common;

namespace API.Implementations.Common
{
    public class FileReader : IFileReader
    {
        public FileStream GetFileStream(string path)
        {
            return File.OpenRead(path);
        }
    }
}
