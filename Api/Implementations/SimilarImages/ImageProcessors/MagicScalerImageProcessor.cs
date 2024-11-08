using System.Drawing;
using CommunityToolkit.HighPerformance.Buffers;
using Core.Entities.Files;
using Core.Entities.Images;
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
    private static readonly IPixelTransform BgrPixelTransform = new FormatConversionTransform(PixelFormats.Bgr24bpp);

    private static readonly ProcessImageSettings ProcessImageSettings = new()
    {
        ColorProfileMode = ColorProfileMode.ConvertToSrgb, ResizeMode = CropScaleMode.Stretch,
        OrientationMode = OrientationMode.Normalize, HybridMode = HybridScaleMode.FavorQuality, Sharpen = false
    };

    private static Rectangle _area = new(0, 0, 0, 0);

    private readonly IColorSpaceConverter _colorSpaceConverter;

    public MagicScalerImageProcessor(IColorSpaceConverter colorSpaceConverter)
    {
        _colorSpaceConverter = colorSpaceConverter;
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

    public bool GenerateThumbnail(string imagePath, int width, int height, Span<float> pixels, ColorSpace colorSpace)
    {
        try
        {
            switch (colorSpace)
            {
                case ColorSpace.Grayscale when pixels.Length < width * height:
                    throw new ArgumentException(
                        $"Not enough space for thumbnail. Required buffer size is {width * height}",
                        nameof(pixels));
                case ColorSpace.Lab or ColorSpace.Rgb when pixels.Length < width * height * 3:
                    throw new ArgumentException(
                        $"Not enough space for thumbnail. Required buffer size is {width * height * 3}",
                        nameof(pixels));
            }

            if (_area.Width != width || _area.Height != height)
            {
                _area.Width = width;
                _area.Height = height;

                ProcessImageSettings.Width = _area.Width;
                ProcessImageSettings.Height = _area.Height;

                ProcessImageSettings.HybridMode = _area.Width * _area.Height > 65536
                    ? HybridScaleMode.FavorSpeed
                    : HybridScaleMode.FavorQuality;
            }

            using var pipeline = MagicImageProcessor.BuildPipeline(imagePath, ProcessImageSettings);

            return GenerateThumbnail(pipeline, pixels, colorSpace);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool GenerateThumbnail(ProcessedImage image, int width, int height, Span<float> pixels,
        ColorSpace colorSpace)
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

        return GenerateThumbnail(pipeline, pixels, colorSpace);
    }

    private bool GenerateThumbnail(ProcessingPipeline imageProcessingPipeline, Span<float> pixels,
        ColorSpace colorSpace)
    {
        try
        {
            if (colorSpace is ColorSpace.Grayscale)
            {
                imageProcessingPipeline.AddTransform(GreyPixelTransform);
                using var tempGrayPixels = SpanOwner<byte>.Allocate(_area.Width * _area.Height);
                imageProcessingPipeline.PixelSource.CopyPixels(_area, _area.Width, tempGrayPixels.Span);

                for (var i = 0; i < _area.Width * _area.Height; i++)
                    pixels[i] = tempGrayPixels.Span[i];

                return true;
            }

            imageProcessingPipeline.AddTransform(BgrPixelTransform);
            var stride = _area.Width * 3;

            using var tempPixels = MemoryOwner<byte>.Allocate(stride * _area.Height);
            imageProcessingPipeline.PixelSource.CopyPixels(_area, stride, tempPixels.Span);

            if (colorSpace is ColorSpace.Lab)
            {
                for (var i = 0; i < tempPixels.Length; i += 3)
                {
                    (tempPixels.Span[i], tempPixels.Span[i + 2]) = (tempPixels.Span[i + 2], tempPixels.Span[i]);
                }

                return _colorSpaceConverter.GetPixelsInLabColorSpace(tempPixels.Memory, _area.Width, _area.Height,
                    pixels);
            }

            // Colorspace is Rgb
            var imageSize = tempPixels.Length / 3;
            for (var i = 0; i < tempPixels.Length; i += 3)
            {
                pixels[i / 3] = tempPixels.Span[i + 2];
                pixels[imageSize + i / 3] = tempPixels.Span[i + 1];
                pixels[2 * imageSize + i / 3] = tempPixels.Span[i];
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}