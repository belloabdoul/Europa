using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace API.Common.Entities
{
    public enum FileType
    {
        All = 0,
        Images = 1,
        Audios = 2,
        Videos = 3
    }
    public class SearchParametersDto
    {
        // Should we include subfolders or not
        [Required]
        public bool IncludeSubfolders { get; set; }
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
        [IgnoreDataMember]
        public FileType FilesTypeToSearch { get; set; }
        public SearchParametersDto()
        {
            IncludeSubfolders = true;
            MinSize = 0;
            MaxSize = long.MaxValue;
            IncludedFileTypes = [];
            ExcludedFileTypes = [];
            DefaultExcludedFileTypes = [];
        }

        public SearchParametersDto(bool includeSubfolders, long minSize, long maxSize, ICollection<string> includedFileTypes, ICollection<string> excludedFileTypes, ICollection<string> defaultExcludedFileTypes)
        {
            IncludeSubfolders = includeSubfolders;
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
        }

        public static SearchParametersDto ToResponseDto(this SearchParameters entity)
        {
            return new SearchParametersDto(includeSubfolders: entity.SearchOption == SearchOption.AllDirectories, minSize: entity.MinSize, maxSize: entity.MaxSize, includedFileTypes: entity.IncludedFileTypes, excludedFileTypes: entity.ExcludedFileTypes, defaultExcludedFileTypes: entity.DefaultExcludedFileTypes);
        }
    }
}
