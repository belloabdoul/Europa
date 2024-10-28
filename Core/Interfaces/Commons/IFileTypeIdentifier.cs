using Core.Entities.Files;

namespace Core.Interfaces.Commons;

public interface IFileTypeIdentifier
{
    FileType GetFileType(string path);
}