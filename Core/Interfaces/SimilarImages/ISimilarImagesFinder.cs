using File = Core.Entities.File;

namespace Core.Interfaces.SimilarImages;

public interface ISimilarImagesFinder
{
    Task<IEnumerable<IGrouping<byte[], File>>> FindSimilarImagesAsync(SortedList<string, (long, DateTime)> hypotheticalDuplicates,
        double degreeOfSimilarity, CancellationToken token);
}