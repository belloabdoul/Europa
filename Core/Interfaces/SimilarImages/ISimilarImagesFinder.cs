using File = Core.Entities.File;

namespace Core.Interfaces.SimilarImages
{
    public interface ISimilarImagesFinder
    {
        Task<IEnumerable<IGrouping<string, File>>> FindSimilarImagesAsync(List<string> hypotheticalDuplicates, double degreeOfSimilarity, CancellationToken token);
    }
}
