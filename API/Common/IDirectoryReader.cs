using API.Entities;

namespace API.Common
{
    public interface IDirectoryReader
    {
        string[] GetAllFilesFromFolder(string folder, SearchParametersDto searchParameters, CancellationToken token);
    }
}
