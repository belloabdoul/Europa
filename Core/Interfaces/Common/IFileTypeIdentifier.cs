using Core.Entities;

namespace Core.Interfaces.Common;

public interface IFileTypeIdentifier
{
    FileType GetFileType(string path);
    FileSearchType GetAssociatedSearchType();
}

public delegate List<IFileTypeIdentifier> FileTypeIdentifierResolver(FileSearchType searchType);