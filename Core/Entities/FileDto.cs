// ReSharper disable UnusedAutoPropertyAccessor.Global
namespace Core.Entities
{
    public class FileDto
    {
        // Name of the file
        public string Name { get; set; }

        // Type of the file
        public string Type { get; set; }

        // The full path to the file
        public string Path { get; }

        // Size of the file
        public long Size { get; set; }

        // The last time the file has been modified
        public DateTime DateModified { get; set; }

        public FileDto(string name, string type, string path, long size, DateTime dateModified)
        {
            Name = name;
            Type = type;
            Path = path;
            Size = size;
            DateModified = dateModified;
        }
    }

    public static class FileDtoMapping
    {
        public static FileDto ToResponseDto(this File file)
        {
            return new FileDto(Path.GetFileName(file.Path), Path.GetExtension(file.Path),
                file.Path, file.Size, file.DateModified);
        }
    }
}