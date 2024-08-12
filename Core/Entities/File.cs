// ReSharper disable CollectionNeverUpdated.Global
// ReSharper disable EntityFramework.ModelValidation.UnlimitedStringLength

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace Core.Entities;

public class File
{
    // Constructors
    public File()
    {
    }

    public File(FileInfo file, string hash)
    {
        Hash = hash;
        Size = file.Length;
        Path = file.FullName;
        DateModified = System.IO.File.GetLastWriteTime(file.FullName).ToUniversalTime();
    }

    // The full path to the file
    public string Path { get; init; }

    // Size of the file
    public long Size { get; init; }

    // The last time the file has been modified
    public DateTime DateModified { get; init; }

    public string Hash { get; init; }
}