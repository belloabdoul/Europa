using Core.Entities;

namespace Core.Interfaces.Common;

public interface IDirectoryReader
{
    bool FileExists(string filePath);

    Task<SortedList<string, (long, DateTime)>> GetAllFilesFromFolderAsync(SearchParameters searchParameters, CancellationToken cancellationToken);
}