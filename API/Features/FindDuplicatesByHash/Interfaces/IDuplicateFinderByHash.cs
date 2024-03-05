using File = API.Common.Entities.File;

namespace API.Features.FindDuplicatesByHash.Interfaces
{
    public interface IDuplicateFinderByHash
    {
        Task<IEnumerable<IGrouping<string, File>>> FindDuplicateByHash(List<string> hypotheticalDuplicates, CancellationToken token);
    }
}
