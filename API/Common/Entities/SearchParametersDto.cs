using System.ComponentModel.DataAnnotations;

namespace API.Common.Entities
{
    public class SearchParametersDto
    {
        // Should we include subfolders or not
        [Required(ErrorMessage = "You need to specify wether subfolders should be included or not.")]
        public bool IncludeSubfolders { get; set; }
        // The category of the files we will be processing
        [Required(ErrorMessage = "You need to specify if you want to process all files or only either audio or image files.")]
        [EnumDataType(typeof(FileType))]
        public FileType FileTypeToSearch { get; set; }
        // The minimum size for files to include in search
        public long MinSize { get; set; }
        // The maximum size for files to include
        public long MaxSize { get; set; }
        // The file types to include. With this excluded files types are disabled
        public ICollection<string> IncludedFileTypes { get; set; } = [];
        // The file types to exclude. Only work if no file types are included
        public ICollection<string> ExcludedFileTypes { get; set; } = [];
        // The file types excluded by default.
        public ICollection<string> DefaultExcludedFileTypes { get; set; } = [];
        public SearchParametersDto()
        {
            IncludeSubfolders = true;
            FileTypeToSearch = FileType.All;
            MinSize = 0;
            MaxSize = long.MaxValue;
            IncludedFileTypes = [];
            ExcludedFileTypes = [];
            DefaultExcludedFileTypes = [];
        }

        public SearchParametersDto(bool includeSubfolders, long minSize, long maxSize, ICollection<string> includedFileTypes, ICollection<string> excludedFileTypes, ICollection<string> defaultExcludedFileTypes, FileType fileTypeToSearch)
        {
            IncludeSubfolders = includeSubfolders;
            FileTypeToSearch = fileTypeToSearch;
            MinSize = minSize;
            MaxSize = maxSize;
            IncludedFileTypes = includedFileTypes;
            ExcludedFileTypes = excludedFileTypes;
            DefaultExcludedFileTypes = defaultExcludedFileTypes;
        }
    }

    public static class SearchParametersDtoMapping
    {
        public static void CopyDtoToSearchParametersEntity(this SearchParametersDto dto, SearchParameters entity)
        {
            entity.SearchOption = dto.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            entity.MinSize = dto.MinSize;
            entity.MaxSize = dto.MaxSize;
            entity.IncludedFileTypes = dto.IncludedFileTypes;
            entity.ExcludedFileTypes = dto.ExcludedFileTypes;
            entity.DefaultExcludedFileTypes = dto.DefaultExcludedFileTypes;
            entity.FileTypeToSearch = dto.FileTypeToSearch;
        }
    }
}
