using Blake3;
using File = Core.Entities.File;

namespace Core.Interfaces.DuplicatesByHash;

public interface IDuplicateByHashFinder
{
    Task<IEnumerable<IGrouping<Hash, File>>> FindDuplicateByHash(HashSet<string> hypotheticalDuplicates,
        CancellationToken token);
}