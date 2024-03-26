namespace Core.Entities
{
    public class DuplicatesResponse
    {
        public List<List<FileDto>> DuplicatesGroups { get; set; }
    }

    public static class DuplicatesResponseMapping
    {
        public static DuplicatesResponse ToResponseDTO(this IEnumerable<IGrouping<string, File>> duplicatesGroups)
        {
            DuplicatesResponse response = new DuplicatesResponse();
            response.DuplicatesGroups = duplicatesGroups.Select(group => group.Select(file => file.ToResponseDto()).ToList()).ToList();
            return response;
        }
    }
}
