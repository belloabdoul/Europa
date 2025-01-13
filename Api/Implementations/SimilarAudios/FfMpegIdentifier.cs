using Core.Entities.Files;
using Core.Interfaces.Commons;
using Sdcb.FFmpeg.Common;
using Sdcb.FFmpeg.Formats;
using AVMediaType = Sdcb.FFmpeg.Raw.AVMediaType;

namespace Api.Implementations.SimilarAudios;

public class FfMpegIdentifier : IFileTypeIdentifier
{
    public FileType GetFileType(string path)
    {
        using var fileInfo = OpenContext(path);
        if (fileInfo == null)
            return FileType.File;

        fileInfo.LoadStreamInfo();
        
        var audioStream = fileInfo.FindBestStreamOrNull(AVMediaType.Audio);
        var videoStream = fileInfo.FindBestStreamOrNull(AVMediaType.Video);
        
        if (!audioStream.HasValue && !videoStream.HasValue)
            return FileType.File;

        if (audioStream.HasValue && videoStream.HasValue)
            return FileType.AudioVideo;
        
        return audioStream.HasValue ? FileType.Audio : FileType.Video;
    }

    private static FormatContext? OpenContext(string path)
    {
        try
        {
            return FormatContext.OpenInputUrl(path);
        }
        catch (FFmpegException)
        {
            return null;
        }
    }
}