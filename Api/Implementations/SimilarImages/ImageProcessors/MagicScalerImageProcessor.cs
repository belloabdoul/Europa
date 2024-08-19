using System.Drawing;
using Core.Entities;
using Core.Interfaces;
using Core.Interfaces.Common;
using Microsoft.IO;
using PhotoSauce.MagicScaler;
using PhotoSauce.MagicScaler.Transforms;
using Sdcb.LibRaw;

namespace Api.Implementations.SimilarImages.ImageProcessors;

public class MagicScalerImageProcessor : IFileTypeIdentifier, IThumbnailGenerator, IMainThumbnailGenerator
{
    private static readonly RecyclableMemoryStreamManager RecyclableMemoryStreamManager = new();

    public FileSearchType GetAssociatedSearchType()
    {
        return FileSearchType.Images;
    }

    public FileType GetFileType(string path)
    {
        FileType fileType;
        try
        {
            var imageInfos = ImageFileInfo.Load(path);
            if (imageInfos.Frames.Count > 1)
                fileType = imageInfos.MimeType!.Contains("webp") || imageInfos.MimeType.Contains("png") ||
                           imageInfos.MimeType.Contains("gif") || imageInfos.MimeType.Contains("avif") ||
                           imageInfos.MimeType.Contains("heic")
                    ? FileType.Animation
                    : FileType.MagicScalerImage;
            else
                fileType = FileType.MagicScalerImage;
        }
        catch (Exception)
        {
            fileType = FileType.CorruptUnknownOrUnsupported;
        }

        return fileType;
    }

    public byte[] GenerateThumbnail(ProcessedImage image, int width, int height)
    {
        using var stream = RecyclableMemoryStreamManager.GetStream(image.AsSpan<byte>());

        using var pipeline = MagicImageProcessor.BuildPipeline(stream,
            new ProcessImageSettings
            {
                Width = width, Height = height, ColorProfileMode = ColorProfileMode.ConvertToSrgb,
                ResizeMode = CropScaleMode.Stretch, OrientationMode = OrientationMode.Normalize,
                HybridMode = HybridScaleMode.FavorSpeed
            });

        pipeline.AddTransform(new FormatConversionTransform(PixelFormats.Grey8bpp));

        var pixels = new byte[width * height];

        pipeline.PixelSource.CopyPixels(new Rectangle(0, 0, width, height), width, pixels);

        return pixels;
    }

    public byte[] GenerateThumbnail(string imagePath, int width, int height)
    {
        try
        {
            using var pipeline = MagicImageProcessor.BuildPipeline(imagePath,
                new ProcessImageSettings
                {
                    Width = width, Height = height, ColorProfileMode = ColorProfileMode.ConvertToSrgb,
                    ResizeMode = CropScaleMode.Stretch, OrientationMode = OrientationMode.Normalize,
                    HybridMode = HybridScaleMode.FavorSpeed
                });

            pipeline.AddTransform(new FormatConversionTransform(PixelFormats.Grey8bpp));

            var pixels = new byte[width * height];

            pipeline.PixelSource.CopyPixels(new Rectangle(0, 0, width, height), width, pixels);

            return pixels;
        }
        catch (Exception)
        {
            return [];
        }
    }
}