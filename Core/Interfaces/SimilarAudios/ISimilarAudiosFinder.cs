using File = Core.Entities.File;

namespace Core.Interfaces.SimilarAudios;

public interface ISimilarAudiosFinder
{
    Task<IEnumerable<IGrouping<string, File>>> FindSimilarAudiosAsync(HashSet<string> hypotheticalDuplicates,
        CancellationToken token);
}