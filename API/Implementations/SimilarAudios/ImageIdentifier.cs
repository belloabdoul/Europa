using Core.Interfaces.Common;
using MediaInfo;
using Microsoft.Extensions.Logging.Abstractions;

namespace API.Implementations.SimilarAudios
{
    public class FileTypeIdentifier
    {
        private readonly string audio = string.Intern("audio");
        private readonly string video = string.Intern("video");
        private readonly string file = string.Intern("file");

        public string GetFileType(string path)
        {
            var media = new MediaInfoWrapper(path, NullLogger.Instance);
            if (media.HasVideo)
                return video;
            else if (media.AudioStreams.Count > 0)
                return audio;
            return file;
        }
    }
}
