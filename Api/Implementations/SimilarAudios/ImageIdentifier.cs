using Core.Entities;
using Core.Interfaces.Common;
using MediaInfoLib;

namespace Api.Implementations.SimilarAudios;

public class FileTypeIdentifier : IFileTypeIdentifier
{
    public FileType GetFileType(string path)
    {
        using var mediaInfoLib = new MediaInfo();
        mediaInfoLib.WithOpen(path);
        if (mediaInfoLib.Count_Get(StreamKind.Video) > 0)
            return FileType.Video;
        return mediaInfoLib.Count_Get(StreamKind.Audio) > 0 ? FileType.Audio : FileType.File;
    }

    public FileSearchType GetAssociatedSearchType()
    {
        return FileSearchType.Audios;
    }
}