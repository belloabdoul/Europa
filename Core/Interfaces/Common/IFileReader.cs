namespace Core.Interfaces.Common
{
    public interface IFileReader
    {
        FileStream GetFileStream(string path);
    }
}
