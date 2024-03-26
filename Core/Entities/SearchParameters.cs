namespace Core.Entities
{
    public class SearchParameters
    {
        // Should we include subfolders or not
        public SearchOption SearchOption { get; set; }
        // The category of the files we will be processing
        public FileType FileTypeToSearch { get; set; }
        // The minimum size for files to include in search
        public long? MinSize { get; set; }
        // The maximum size for files to include
        public long? MaxSize { get; set; }
        // The file types to include. With this excluded files types are disabled
        public ICollection<string> IncludedFileTypes { get; set; }
        // The file types to exclude. Only work if no file types are included
        public ICollection<string> ExcludedFileTypes { get; set; }
        // The file types excluded by default.
        public ICollection<string> DefaultExcludedFileTypes { get; set; }

        public SearchParameters()
        {
            SearchOption = SearchOption.AllDirectories;
            MinSize = 0;
            MaxSize = 0;
            IncludedFileTypes = [];
            ExcludedFileTypes = [];
            DefaultExcludedFileTypes = [];
            FileTypeToSearch = FileType.All;
        }
    }
}
