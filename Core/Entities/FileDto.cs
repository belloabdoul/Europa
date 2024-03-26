namespace Core.Entities
{
    public class FileDto
    {
        // Name of the file
        public string Name { get; set; }
        // Type of the file
        public string Type { get; set; }
        // The full path to the file
        public string Path { get; set; }
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
        public static void CopyDtoToFileEntity(this FileDto dto, File entity)
        {
            entity.Path = dto.Path;
            entity.Size = dto.Size;
            entity.DateModified = dto.DateModified;
        }

        public static FileDto ToResponseDto(this File entity)
        {
            return new FileDto(Path.GetFileNameWithoutExtension(entity.Path), Path.GetExtension(entity.Path)[1 ..] ,entity.Path, entity.Size, entity.DateModified);
        }
    }
}
