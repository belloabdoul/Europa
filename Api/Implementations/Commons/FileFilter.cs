using System.Buffers;

namespace Api.Implementations.Commons;

public static class FileFilter
{
    public static bool IsFileToBeIncluded(string extension, SearchValues<string> extensionsToInclude)
    {
        return extensionsToInclude.GetType().Name[..^2] == "EmptySearchValues" ||
               extensionsToInclude.Contains(extension) || extensionsToInclude.Contains(extension[1..]);
    }

    public static bool IsFileToBeExcluded(string extension, SearchValues<string> extensionsToExclude)
    {
        return extensionsToExclude.GetType().Name[..^2] != "EmptySearchValues" &&
               (extensionsToExclude.Contains(extension) || extensionsToExclude.Contains(extension[1..]));
    }

    public static bool IsFileSizeInRange(long fileSize, long? minSize, long? maxSize)
    {
        if (minSize.HasValue && maxSize.HasValue)
            return fileSize >= minSize && fileSize <= maxSize;
        if (minSize.HasValue)
            return fileSize >= minSize;
        if (maxSize.HasValue)
            return fileSize <= maxSize;
        return true;
    }
}