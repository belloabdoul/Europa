using System.Drawing;
using Core.Entities.Files;
using Core.Interfaces.Commons;
using Core.Interfaces.SimilarImages;
using Microsoft.IO;
using PhotoSauce.MagicScaler;
using PhotoSauce.MagicScaler.Transforms;
using Sdcb.LibRaw;

namespace Api.Implementations.SimilarImages.ImageProcessors;

public class MagicScalerImageProcessor : IFileTypeIdentifier, IThumbnailGenerator, IMainThumbnailGenerator
{
    private static readonly RecyclableMemoryStreamManager RecyclableMemoryStreamManager = new();

    private static readonly IPixelTransform GreyPixelTransform = new FormatConversionTransform(PixelFormats.Grey8bpp);

    private static readonly ProcessImageSettings ProcessImageSettings = new()
    {
        ColorProfileMode = ColorProfileMode.ConvertToSrgb,
        ResizeMode = CropScaleMode.Stretch, OrientationMode = OrientationMode.Normalize,
        HybridMode = HybridScaleMode.FavorQuality,
    };

    private static Rectangle _area = new(0, 0, 0, 0);

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

    public bool GenerateThumbnail(string imagePath, int width, int height, Span<byte> pixels)
    {
        if (_area.Width != width || _area.Height != height)
        {
            _area.Width = width;
            _area.Height = height;

            ProcessImageSettings.Width = _area.Width;
            ProcessImageSettings.Height = _area.Height;
        }

        using var pipeline = MagicImageProcessor.BuildPipeline(imagePath, ProcessImageSettings);

        return GenerateThumbnail(pipeline, pixels);
    }

    public bool GenerateThumbnail(ProcessedImage image, int width, int height, Span<byte> pixels)
    {
        using var stream = RecyclableMemoryStreamManager.GetStream(image.AsSpan<byte>());

        if (_area.Width != width || _area.Height != height)
        {
            _area.Width = width;
            _area.Height = height;

            ProcessImageSettings.Width = _area.Width;
            ProcessImageSettings.Height = _area.Height;
        }

        using var pipeline = MagicImageProcessor.BuildPipeline(stream, ProcessImageSettings);

        return GenerateThumbnail(pipeline, pixels);
    }

    private static bool GenerateThumbnail(ProcessingPipeline imageProcessingPipeline, Span<byte> pixels)
    {
        try
        {
            imageProcessingPipeline.AddTransform(GreyPixelTransform);

            imageProcessingPipeline.PixelSource.CopyPixels(_area, _area.Width, pixels);

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}