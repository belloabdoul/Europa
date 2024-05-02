using Core.Entities;
using Pgvector;
using System.Collections.Concurrent;
using File = Core.Entities.File;

namespace Database.Interfaces
{
    public interface IDbHelpers
    {
        Task GetCachedHashAsync(ConcurrentQueue<File> filesWithoutImageHash, CancellationToken token);
        Task<bool> CacheHashAsync(ConcurrentQueue<File> filesToCache, CancellationToken cancellationToken);

        Task<List<string>> GetSimilarImagesGroupsAssociatedToAsync(string currentFile,
            CancellationToken cancellationToken);

        Task<List<Similarity>> GetSimilarImagesGroups(Vector imageHash, double threshold,
            List<string> imagesGroupsAlreadyDone, CancellationToken cancellationToken);

        Task SetSimilaritiesAsync(IEnumerable<Similarity> similarities,
            CancellationToken cancellationToken);

        Task<List<string>> GetSimilarImagesGroupsInRangeAsync(string currentFile, double threshold,
            CancellationToken cancellationToken);
    }
}