namespace Core.Entities;

public class DuplicatesResponse
{
    public List<List<FileDto>>? DuplicatesGroups { get; set; }
}

public static class DuplicatesResponseMapping
{
    public static DuplicatesResponse ToResponseDto(this IEnumerable<IGrouping<byte[], File>> duplicatesGroups)
    {
        var response = new DuplicatesResponse
        {
            DuplicatesGroups = duplicatesGroups.Select(group => group.Select(file => file.ToResponseDto()).ToList())
                .ToList()
        };
        return response;
    }
}