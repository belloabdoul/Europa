using API.Common.Entities;

namespace API.Common.Interfaces
{
    public interface IDirectoryReader
    {
        bool FileExists(string filePath);

        string[] GetAllFilesFromFolder(List<string> folders, SearchParametersDto searchParameters, CancellationToken token, out List<string> errors);
    }
}
