namespace API.Interfaces.Common
{
    public interface IFileReader
    {
        FileStream GetFileStream(string path);
    }
}
