using Core.Entities;
using Core.Entities.Files;
using Core.Entities.SearchParameters;

namespace Core.Interfaces.Common;

public interface IFileTypeIdentifier
{
    FileType GetFileType(string path);
    FileSearchType AssociatedSearchType { get; }
}