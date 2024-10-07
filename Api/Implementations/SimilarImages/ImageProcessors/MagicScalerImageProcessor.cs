using System.Drawing;
using System.Runtime.CompilerServices;
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

    public FileSearchType AssociatedSearchType => FileSearchType.Images;

    public FileType AssociatedImageType => FileType.MagicScalerImage;

    private static readonly IPixelTransform GreyPixelTransform = new FormatConversionTransform(PixelFormats.Grey8bpp);

    private static readonly ProcessImageSettings ProcessImageSettings = new()
    {
        ColorProfileMode = ColorProfileMode.ConvertToSrgb,
        ResizeMode = CropScaleMode.Stretch, OrientationMode = OrientationMode.Normalize,
        HybridMode = HybridScaleMode.FavorSpeed,
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> GenerateThumbnail(ProcessedImage image, int width, int height, Span<byte> pixels)
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

        pipeline.AddTransform(GreyPixelTransform);

        pipeline.PixelSource.CopyPixels(_area, _area.Width, pixels);

        return ValueTask.FromResult(true);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask<bool> GenerateThumbnail(string imagePath, int width, int height, Span<byte> pixels)
    {
        try
        {
            if (_area.Width != width || _area.Height != height)
            {
                _area.Width = width;
                _area.Height = height;

                ProcessImageSettings.Width = _area.Width;
                ProcessImageSettings.Height = _area.Height;
            }

            using var pipeline = MagicImageProcessor.BuildPipeline(imagePath, ProcessImageSettings);

            pipeline.AddTransform(GreyPixelTransform);

            pipeline.PixelSource.CopyPixels(_area, _area.Width, pixels);
            return ValueTask.FromResult(true);
        }
        catch (Exception)
        {
            return ValueTask.FromResult(false);
        }
    }
}