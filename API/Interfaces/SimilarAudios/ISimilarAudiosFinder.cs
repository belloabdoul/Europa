using File = API.Common.Entities.File;

namespace API.Interfaces.SimilarAudios
{
    public interface ISimilarAudiosFinder
    {
        Task<IEnumerable<IGrouping<string, File>>> FindSimilarAudiosAsync(List<string> hypotheticalDuplicates, CancellationToken token);
    }
}
