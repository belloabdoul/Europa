using Core.Interfaces.Common;
using Microsoft.Win32.SafeHandles;

namespace API.Implementations.Common;

public class FileReader : IFileReader
{
    public FileStream GetFileStream(string path, int bufferSize = 0, bool isAsync = false)
    {
        return isAsync
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan)
            : new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize,
                FileOptions.SequentialScan);
    }

    public SafeFileHandle GetFileHandle(string path, bool isAsync = false)
    {
        return isAsync
            ? File.OpenHandle(path, options: FileOptions.SequentialScan | FileOptions.Asynchronous)
            : File.OpenHandle(path, options: FileOptions.SequentialScan);
    }
}