using MediaInfo;
using Microsoft.Extensions.Logging.Abstractions;

namespace API.Implementations.SimilarAudios;

public class FileTypeIdentifier
{
    private readonly string audio = string.Intern("audio");
    private readonly string file = string.Intern("file");
    private readonly string video = string.Intern("video");

    public string GetFileType(string path)
    {
        var media = new MediaInfoWrapper(path, NullLogger.Instance);
        if (media.HasVideo)
            return video;
        if (media.AudioStreams.Count > 0)
            return audio;
        return file;
    }
}