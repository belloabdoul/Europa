namespace API.Common.Entities
{
    public class File
    {
        // The full path to the file
        public string Path { get; set; }
        // Size of the file
        public long Size { get; set; }
        // The last time the file has been modified
        public DateTime DateModified { get; set; }
        // The hash of the file
        public string FinalHash { get; set; }
        // The original hash of the file
        public string OriginalHash { get; set; }

        public File(FileInfo file, string finalHash)
        {
            Size = file.Length;
            Path = file.FullName;
            DateModified = System.IO.File.GetLastWriteTime(file.FullName);
            OriginalHash = finalHash;
            FinalHash = finalHash;
        }

        public File(FileInfo file, string originalHash, string finalHash)
        {
            Size = file.Length;
            Path = file.FullName;
            DateModified = System.IO.File.GetLastWriteTime(file.FullName);
            OriginalHash = originalHash;
            FinalHash = finalHash;
        }
    }
}
