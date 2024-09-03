using Blake3;

namespace Core.Entities;

public class File
{
    // The full path to the file
    public string Path { get; init; }

    // Size of the file
    public long Size { get; init; }

    // The last time the file has been modified
    public DateTime DateModified { get; init; }

    public Hash Hash { get; init; }
}