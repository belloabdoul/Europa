using System.Collections.Concurrent;
using File = API.Common.Entities.File;

namespace API.Features.FindSimilarImages.Interfaces
{
    public interface ISimilarImagesFinder
    {
        Task<(IEnumerable<IGrouping<string, File>>, ConcurrentQueue<string>)> /*SortedDictionary<string, List<string>>*/ FindSimilarImagesAsync(List<string> hypotheticalDuplicates, CancellationToken token);
    }
}
