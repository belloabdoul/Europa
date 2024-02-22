using API.Entities;

namespace API.Features.FindDuplicatesByHash
{
    public interface IDuplicateFinderByHash
    {
        Task<IEnumerable<IGrouping<string, FileDto>>> FindDuplicateByHashAsync(List<string> hypotheticalDuplicates, CancellationToken token);
    }
}
