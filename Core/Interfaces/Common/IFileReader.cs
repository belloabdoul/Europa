using Microsoft.Win32.SafeHandles;

namespace Core.Interfaces.Common;

public interface IFileReader
{
    FileStream GetFileStream(string path, int bufferSize = 0, bool isAsync = false);
    SafeFileHandle GetFileHandle(string path, bool isAsync = false);
}