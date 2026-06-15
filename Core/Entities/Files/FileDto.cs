// ReSharper disable UnusedAutoPropertyAccessor.Global

using System.Diagnostics.CodeAnalysis;

namespace Core.Entities.Files;

[SuppressMessage("ReSharper", "UnusedMember.Global")]
public class FileDto(string name, string type, string path, long size, DateTime dateModified)
{
    // Name of the file
    public string Name { get; set; } = name;

    // Type of the file
    public string Type { get; set; } = type;

    // The full path to the file
    public string Path { get; } = path;

    // Size of the file
    public long Size { get; set; } = size;

    // The last time the file has been modified
    public DateTime DateModified { get; set; } = dateModified;
}

public static class FileDtoMapping
{
    public static FileDto ToResponseDto(this File file)
    {
        return new FileDto(Path.GetFileName(file.Path), Path.GetExtension(file.Path),
            file.Path, file.Size, file.DateModified);
    }
}