using Core.Interfaces.Common;
using SkiaSharp;

namespace API.Implementations.SimilarImages
{
    public class ImageIdentifier : IFileTypeIdentifier
    {
        private readonly string animatedWebp = string.Intern("animation/webp");
        private readonly string animatedImage = string.Intern("animation");
        private readonly string imageGif = string.Intern("image/gif");
        private readonly string image = string.Intern("image");

        public string GetFileType(string path)
        {
            using var codec = SKCodec.Create(path);
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
                return $"File {path} is corrupt";
            }
        }
    }
}
