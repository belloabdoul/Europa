namespace Core.Entities
{
    public class File
    {
        // The full path to the file
        public string Path { get; set; }
        // Size of the file
        public long Size { get; set; }
        // The type of the file
        public string Type { get; set; }
        // The position of the file in the search queue
        public int Position { get; set; }
        // The last time the file has been modified
        public DateTime DateModified { get; set; }
        // The hash of the file
        public string Hash { get; set; }
        public File(FileInfo file, string hash)
        {
            Size = file.Length;
            Path = file.FullName;
            DateModified = System.IO.File.GetLastWriteTime(file.FullName);
            Hash = hash;
            Type = string.Empty;
        }

        public File(string path, string type)
        {
            Path = path;
            Type = string.Empty;
            Hash = string.Empty;
        }
    }
}
