using API.Common.Entities;
using File = API.Common.Entities.File;

namespace API.Features.FindDuplicatesByHash.Entities
{
    public class DuplicatesResponse
    {
        public List<List<FileDto>> DuplicatesGroups { get; set; }
        public List<string> Errors { get; set; }

        public DuplicatesResponse(IEnumerable<IGrouping<string, File>> duplicatesGroups, List<string> errors)
        {
            DuplicatesGroups = duplicatesGroups.Select(group => group.Select(file => file.ToResponseDto()).ToList()).ToList();
            Errors = errors;
        }
    }

    public static class DuplicatesResponseMapping
    {
        public static DuplicatesResponse ToResponse(this IEnumerable<IGrouping<string, File>> duplicatesGroups, List<string> errors)
        {
            return new DuplicatesResponse(duplicatesGroups, errors);
        }
    }
}
