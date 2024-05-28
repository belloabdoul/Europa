using Blake3;
using File = Core.Entities.File;

namespace Core.Interfaces.DuplicatesByHash;

public interface IDuplicateByHashFinder
{
    Task<IEnumerable<IGrouping<byte[], File>>> FindDuplicateByHash(List<string> hypotheticalDuplicates,
        CancellationToken token);
}