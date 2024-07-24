namespace API.Implementations.Common;

public static class FileFilter
{
    public static bool IsFileToBeIncluded(string extension, string[] extensionsToInclude)
    {
        return extensionsToInclude.Length == 0 ||
               extensionsToInclude.Contains(extension, StringComparer.InvariantCultureIgnoreCase);
    }

    public static bool IsFileToBeExcluded(string extension, string[] extensionsToExclude)
    {
        return extensionsToExclude.Length != 0 &&
               extensionsToExclude.Contains(extension, StringComparer.InvariantCultureIgnoreCase);
    }

    public static bool IsFileSizeInRange(long fileSize, long? minSize, long? maxSize)
    {
        if(minSize.HasValue && maxSize.HasValue)
            return fileSize >= minSize && fileSize <= maxSize;
        if(minSize.HasValue)
            return fileSize >= minSize;
        if(maxSize.HasValue)
            return fileSize <= maxSize;
        return true;
    }
}