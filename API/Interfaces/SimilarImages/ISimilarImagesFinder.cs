using File = API.Common.Entities.File;

namespace API.Interfaces.SimilarImages
{
    public interface ISimilarImagesFinder
    {
        Task<(IEnumerable<IGrouping<string, File>>, List<string>)> FindSimilarImagesAsync(List<string> hypotheticalDuplicates, CancellationToken token);
    }
}
