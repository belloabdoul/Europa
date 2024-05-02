using File = Core.Entities.File;

namespace Core.Interfaces.DuplicatesByHash
{
    public interface IDuplicateByHashFinder
    {
        Task<IEnumerable<IGrouping<string, File>>> FindDuplicateByHash(List<string> hypotheticalDuplicates, CancellationToken token);
    }
}
