namespace API.Entities
{
    public class FileDto
    {
        // The full path to the file
        public string Path { get; set; }
        // Size of the file
        public long Size { get; set; }
        // The last time the file has been modified
        public DateTime DateModified { get; set; }
        // The hash of the file
        public string Hash { get; set; }

        public FileDto()
        {
            Path = string.Empty;
            Size = 0;
            DateModified = DateTime.MinValue;
            Hash = string.Empty;
        }

        public FileDto(string path, long size, DateTime dateModified, string hash)
        {
            Path = path;
            Size = size;
            DateModified = dateModified;
            Hash = hash;
        }

        public FileDto(FileInfo fileInfo, string hash)
        {
            Path = fileInfo.FullName;
            Size = fileInfo.Length;
            DateModified = fileInfo.LastWriteTime;
            Hash = hash;
        }
    }

    public static class FileDtoMapping
    {
        public static void CopyDtoToFileEntity(this FileDto dto, File entity)
        {
            entity.Path = dto.Path;
            entity.Size = dto.Size;
            entity.DateModified = dto.DateModified;
            entity.Hash = dto.Hash;
        }

        public static FileDto ToResponseDto(this File entity)
        {
            return new FileDto(path: entity.Path, size: entity.Size, dateModified: entity.DateModified, hash: entity.Hash);
        }
    }
}
