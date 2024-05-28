using System.Runtime.InteropServices;
using Core.Entities;
using Core.Interfaces.Common;
using SkiaSharp;

namespace API.Implementations.SimilarImages;

public class ImageIdentifier : IFileTypeIdentifier
{
    public FileType GetFileType(string path)
    {
        // If on Windows remove the 260 characters limitation
        using var codec = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? SKCodec.Create(Utilities.GetValidPath(path))
            : SKCodec.Create(path);
        if (codec == null)
            return FileType.Corrupt;
        if (codec.FrameCount > 1)
            return codec.EncodedFormat == SKEncodedImageFormat.Webp ? FileType.WebpAnimation : FileType.Animation;
        return codec.EncodedFormat == SKEncodedImageFormat.Gif ? FileType.GifImage : FileType.Image;
    }
}