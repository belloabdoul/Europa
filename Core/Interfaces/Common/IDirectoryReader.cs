using Core.Entities;

namespace Core.Interfaces.Common;

public interface IDirectoryReader
{
    Task<string[]> GetAllFilesFromFolderAsync(SearchParameters searchParameters, CancellationToken cancellationToken);

    IEnumerable<string> GetFilesInFolder(string folder, long? minSize, long? maxSize, string[] includedFileTypes,
        string[] excludedFileTypes, bool includeSubFolders = false);
}