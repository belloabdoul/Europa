using Core.Entities.Files;
using Core.Interfaces.Commons;
using MediaInfo;
using Microsoft.Extensions.Logging.Abstractions;

namespace Api.Implementations.SimilarAudios;

public class FileTypeIdentifier : IFileTypeIdentifier
{
    public FileType GetFileType(string path)
    {
        var mediaInfo = new MediaInfoWrapper(path, NullLogger.Instance);
        if (!mediaInfo.Success)
            return FileType.File;

        if (mediaInfo.Duration == 0)
            return FileType.File;
        
        if (mediaInfo.HasVideo)
            return FileType.Video;

        if (mediaInfo.AudioChannels > 0)
            return FileType.Audio;

        return FileType.File;
    }
}