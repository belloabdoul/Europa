using Core.Entities;
using Core.Interfaces.Common;
using PhotoSauce.MagicScaler;
#pragma warning disable CS8602 // Dereference of a possibly null reference.

namespace API.Implementations.SimilarImages
{
    public class ImageIdentifier : IFileTypeIdentifier
    {
        public FileType GetFileType(string path)
        {
            try
            {
                var imageInfo = ImageFileInfo.Load(path);
                if (imageInfo.Frames.Count > 1)
                    return imageInfo.MimeType.Contains("webp", StringComparison.OrdinalIgnoreCase)
                        ? FileType.WebpAnimation
                        : FileType.Animation;
                return imageInfo.MimeType.Contains("gif", StringComparison.OrdinalIgnoreCase)
                    ? FileType.GifImage
                    : FileType.Image;
            }
            catch (ArgumentException)
            {
                return FileType.Corrupt;
            }
            catch (InvalidDataException)
            {
                return FileType.Corrupt;
            }
        }
    }
}