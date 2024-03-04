using API.Common.Interfaces;
using MediaInfo;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace API.Common.Implementations
{
    public class FileTypeIdentifier : IFileTypeIdentifier
    {
        private const string animatedWebp = "animation/webp";
        private const string animatedImage = "animation";
        private const string imageGif = "image/gif";
        private const string image = "image";
        private const string audio = "audio";
        private const string video = "video";
        private const string file = "file";

        public string GetFileType(string path)
        {
            using var fileStream = File.OpenRead(path);
            using var codec = SKCodec.Create(fileStream);
            if (codec != null)
            {
                if (codec.FrameCount > 1)
                {
                    if (codec.EncodedFormat == SKEncodedImageFormat.Webp)
                        return animatedWebp;
                    return animatedImage;
                }
                else if (codec.EncodedFormat == SKEncodedImageFormat.Gif)
                    return imageGif;
                return image;
            }
            else
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
}
