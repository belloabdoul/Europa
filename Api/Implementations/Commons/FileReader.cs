using System.IO.MemoryMappedFiles;
using DotNext.IO.MemoryMappedFiles;
using Microsoft.Win32.SafeHandles;

namespace Api.Implementations.Commons;

public static class FileReader
{
    private const int BufferSize = 1_048_576;

    public static SafeFileHandle GetFileHandle(string path, bool sequential = false, bool isAsync = false)
    {
        var options = FileOptions.None;
        options |= sequential ? FileOptions.SequentialScan : FileOptions.RandomAccess;
        if (isAsync)
            options |= FileOptions.Asynchronous;
        return File.OpenHandle(path, options: options);
    }

    public static ReadOnlySequenceAccessor? GetMemoryMappedFile(SafeFileHandle fileHandle,
        long lengthToRead = long.MaxValue)
    {
        var length = RandomAccess.GetLength(fileHandle);
        if (length == 0)
            return null;
        var memoryMappedFile = MemoryMappedFile.CreateFromFile(fileHandle, null, length, MemoryMappedFileAccess.Read,
            HandleInheritability.None, true);
        var lengthToUse = lengthToRead == long.MaxValue ? length : lengthToRead;
        return new ReadOnlySequenceAccessor(memoryMappedFile,
            lengthToUse > BufferSize ? BufferSize : Convert.ToInt32(lengthToUse),
            lengthToUse);
    }
}