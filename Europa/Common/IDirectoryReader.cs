using Europa.Entities;

namespace Europa.Common
{
    public interface IDirectoryReader
    {
        string[] GetAllFilesFromFolder(string folder, SearchParametersDto searchParameters, CancellationToken token);
    }
}
