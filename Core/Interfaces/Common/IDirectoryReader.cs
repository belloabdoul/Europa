using Core.Entities;

namespace Core.Interfaces.Common;

public interface IDirectoryReader
{
    bool FileExists(string filePath);

    Task<HashSet<string>> GetAllFilesFromFolderAsync(SearchParameters searchParameters, CancellationToken cancellationToken);
}