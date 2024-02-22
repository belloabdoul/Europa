namespace Europa.Entities
{
    public class File
    {
        // Name of the file
        public string? Name { get; set; }
        // Type of the file
        public string? Type { get; set; }
        // Size of the file
        public long Size { get; set; }
        // The full path to the file
        public string Path { get; set; }
        // The last time the file has been modified
        public DateTime DateModified { get; set; }
        // The hash of the file
        public string Hash { get; set; }

        public File(FileInfo file)
        {
            Name = file.Name;
            Type = file.Extension;
            Size = file.Length;
            Path = file.FullName;
            DateModified = System.IO.File.GetLastWriteTime(file.FullName);
            Hash = string.Empty;
        }

        public File(string path, long size, DateTime dateModified, string hash)
        {
            Path = path;
            Size = size;
            DateModified = dateModified;
            Hash = hash;
        }
    }
}
