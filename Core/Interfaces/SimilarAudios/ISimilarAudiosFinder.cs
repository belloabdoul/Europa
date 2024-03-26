using File = Core.Entities.File;

namespace Core.Interfaces.SimilarAudios
{
    public interface ISimilarAudiosFinder
    {
        Task<IEnumerable<IGrouping<string, File>>> FindSimilarAudiosAsync(List<string> hypotheticalDuplicates, CancellationToken token);
    }
}
