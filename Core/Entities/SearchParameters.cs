namespace Core.Entities;

public class SearchParameters
{
    // The folders to process
    public string[] Folders { get; set; } = [];

    // The category of the files we will be processing
    public FileSearchType? FileSearchType { get; set; }

    // The degree of similarity between images (only for images)
    public int? DegreeOfSimilarity { get; set; }

    // Should we include subfolders or not
    public bool IncludeSubfolders { get; set; }

    // The minimum size for files to include in search
    public long? MinSize { get; set; }

    // The maximum size for files to include
    public long? MaxSize { get; set; }

    // The file types to include. With this excluded files types are disabled
    public string[] IncludedFileTypes { get; set; } = [];

    // The file types to exclude. Only work if no file types are included
    public string[] ExcludedFileTypes { get; set; } = [];
}