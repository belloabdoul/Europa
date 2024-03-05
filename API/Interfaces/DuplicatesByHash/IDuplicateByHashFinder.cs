using File = API.Common.Entities.File;

namespace API.Interfaces.DuplicatesByHash
{
    public interface IDuplicateByHashFinder
    {
        Task<IEnumerable<IGrouping<string, File>>> FindDuplicateByHash(List<string> hypotheticalDuplicates, CancellationToken token);
    }
}
