using File = Core.Entities.Files.File;

namespace Core.Interfaces.Commons;

public interface ISimilarFilesFinder
{
    Task<IEnumerable<IGrouping<byte[], File>>> FindSimilarFilesAsync(List<string> hypotheticalDuplicates,
        decimal? degreeOfSimilarity = null, CancellationToken cancellationToken = default);
}