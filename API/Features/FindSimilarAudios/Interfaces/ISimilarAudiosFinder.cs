using File = API.Common.Entities.File;

namespace API.Features.FindSimilarAudios.Interfaces
{
    public interface ISimilarAudiosFinder
    {
        Task<IEnumerable<IGrouping<string, File>>> FindSimilarAudiosAsync(List<string> hypotheticalDuplicates, CancellationToken token);
    }
}
