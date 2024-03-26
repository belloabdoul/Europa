using Core.Entities;

namespace Core.Interfaces.Common
{
    public interface IDirectoryReader
    {
        bool FileExists(string filePath);

        Task<string[]> GetAllFilesFromFolderAsync(List<string> folders, SearchParametersDto searchParameters, CancellationToken token);
    }
}
