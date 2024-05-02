using Core.Entities;

namespace Core.Interfaces.Common
{
    public interface IDirectoryReader
    {
        bool FileExists(string filePath);

        Task<List<string>> GetAllFilesFromFolderAsync(SearchParameters searchParameters, CancellationToken token);
    }
}
