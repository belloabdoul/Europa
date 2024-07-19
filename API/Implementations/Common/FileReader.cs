using Core.Interfaces.Common;
using Microsoft.Win32.SafeHandles;

namespace API.Implementations.Common;

public class FileReader : IFileReader
{
    public FileStream GetFileStream(string path, int bufferSize = 1)
    {
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize);
    }

    public SafeFileHandle GetFileHandle(string path, bool isAsync = false)
    {
        return isAsync
            ? File.OpenHandle(path, options: FileOptions.RandomAccess | FileOptions.Asynchronous)
            : File.OpenHandle(path, options: FileOptions.RandomAccess);
    }
}