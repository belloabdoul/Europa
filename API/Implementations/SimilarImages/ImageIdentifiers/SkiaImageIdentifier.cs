using Core.Entities;
using Core.Interfaces.Common;
using SkiaSharp;

namespace API.Implementations.SimilarImages.ImageIdentifiers;

public class SkiaImageIdentifier: IFileTypeIdentifier
{
    public FileType GetFileType(string path)
    {
        using var data = SKData.Create(path);
        using var codec = SKCodec.Create(data);
        if (codec == null)
            return FileType.Corrupt;
        if (codec.FrameCount > 1)
            return codec.EncodedFormat == SKEncodedImageFormat.Webp ? FileType.WebpAnimation : FileType.Animation;
        return codec.EncodedFormat == SKEncodedImageFormat.Gif ? FileType.GifImage : FileType.Image;    }
}