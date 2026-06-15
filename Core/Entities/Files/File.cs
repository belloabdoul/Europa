namespace Core.Entities.Files;

public class File
{
    // The full path to the file
    public string Path { get; init; } = string.Empty;

    // Size of the file
    public long Size { get; init; }

    // The last time the file has been modified
    public DateTime DateModified { get; init; } = DateTime.MinValue;

    public byte[] Hash { get; init; } = [];
}