using File = Core.Entities.File;

namespace Core.Interfaces.SimilarAudios;

public interface ISimilarAudiosFinder
{
    Task<IEnumerable<IGrouping<byte[], File>>> FindSimilarAudiosAsync(IList<string> hypotheticalDuplicates,
        CancellationToken token);
}