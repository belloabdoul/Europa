using API.Common.Interfaces;
using MediaInfo;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace API.Common.Implementations
{
    public class FileTypeIdentifier : IFileTypeIdentifier
    {
        public string GetFileType(string path)
        {
            using var fileStream = File.OpenRead(path);
            using var codec = SKCodec.Create(fileStream);
            if (codec != null)
            {
                if (codec.FrameCount > 1)
                {
                    if (codec.EncodedFormat == SKEncodedImageFormat.Webp)
                        return "animation/webp";
                    return "animation";
                }
                else if (codec.EncodedFormat == SKEncodedImageFormat.Gif)
                    return "image/gif";
                return "image";
            }
            else
            {
                var media = new MediaInfoWrapper(path, NullLogger.Instance);
                if (media.HasVideo)
                    return "video";
                else if (media.AudioStreams.Count > 0)
                    return "audio";
                return "file";
            }
        }
    }
}
