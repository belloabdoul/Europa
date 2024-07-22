using Blake3;
using File = Core.Entities.File;

namespace Core.Interfaces.SimilarAudios;

public interface ISimilarAudiosFinder
{
    Task<IEnumerable<IGrouping<Hash, File>>> FindSimilarAudiosAsync(HashSet<string> hypotheticalDuplicates,
        CancellationToken token);
}