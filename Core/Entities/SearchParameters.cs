using System.ComponentModel.DataAnnotations;

namespace Core.Entities;

public class SearchParameters
{ 
    // The folders to process
    [Required(ErrorMessage = "At least one folder must be set")]
    public string[] Folders { get; set; }

    // Should we include subfolders or not
    [Required(ErrorMessage = "No folder inclusion policy set")]
    public bool IncludeSubfolders { get; set; }

    // The category of the files we will be processing
    [Required(ErrorMessage = "The types of file to process must be set")]
    public FileSearchType FileSearchType { get; set; }

    // The degree of similarity between images (only for images)
    [Range(0.7, 1, ConvertValueInInvariantCulture = true,
        ErrorMessage = "The degree of similarity must be between 0.7 and 1", ParseLimitsInInvariantCulture = true)]
    public double? DegreeOfSimilarity { get; set; }

    // The minimum size for files to include in search
    public long? MinSize { get; set; }

    // The maximum size for files to include
    public long? MaxSize { get; set; }

    // The file types to include. With this excluded files types are disabled
    public string[] IncludedFileTypes { get; set; }

    // The file types to exclude. Only work if no file types are included
    public string[] ExcludedFileTypes { get; set; }
}