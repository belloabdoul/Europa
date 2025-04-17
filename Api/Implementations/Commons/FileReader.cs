using Microsoft.Win32.SafeHandles;

namespace Api.Implementations.Commons;

public static class FileReader
{
    public static SafeFileHandle GetFileHandle(string path, bool sequential = false, bool isAsync = false)
    {
        var fileOptions = FileOptions.None;
        fileOptions |= sequential ? FileOptions.SequentialScan : FileOptions.RandomAccess;
        if (isAsync)
            fileOptions |= FileOptions.Asynchronous;
        return File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read, fileOptions);
    }
}