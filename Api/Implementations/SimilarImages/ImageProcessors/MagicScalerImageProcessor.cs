using System.Collections.Concurrent;
using System.Drawing;
using System.Numerics;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using CommunityToolkit.HighPerformance.Buffers;
using Core.Entities.Files;
using Core.Entities.Images;
using Core.Interfaces.Commons;
using Core.Interfaces.SimilarImages;
using DotNext;
using PhotoSauce.MagicScaler;
using PhotoSauce.MagicScaler.Transforms;
using Sdcb.LibRaw;

namespace Api.Implementations.SimilarImages.ImageProcessors;

public sealed class MagicScalerImageProcessor(IPolarCoordinatesConverter polarCoordinatesConverter)
    : IFileTypeIdentifier, IThumbnailGenerator, IMainThumbnailGenerator
{
    private static readonly IPixelTransform GreyPixelTransform = new FormatConversionTransform(PixelFormats.Grey8bpp);
    private static readonly IPixelTransform BgrPixelTransform = new FormatConversionTransform(PixelFormats.Bgr24bpp);
    private static readonly IPixelTransform GaussianFilter = new GaussianBlurTransform(2);


    private static readonly IPixelTransform BgrToRgb =
        new ColorMatrixTransform(new Matrix4x4
        {
            // R, G, B
            M13 = 1.0f,
            M22 = 1.0f,
            M31 = 1.0f,
            M44 = 1.0f
        });

    private readonly ConcurrentDictionary<Vector2, ProcessImageSettings> _processImageSettings = new();
    private readonly ConcurrentDictionary<Vector2, Rectangle> _thumbnailSizes = new();

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

    public Result<bool> GenerateThumbnail<T>(string imagePath, int width, int height, ColorSpace colorSpace,
        bool inPolarCoordinates, Span<T> thumbnail) where T : struct, IBinaryFloatingPointIeee754<T>, IConvertible
    {
        var expected = width * height * Unsafe.As<ColorSpace, int>(ref colorSpace);

        if (expected == 0)
            return new Result<bool>(new ArgumentOutOfRangeException(nameof(colorSpace), "Unexpected color space"));
        if (thumbnail.Length < expected)
            return new Result<bool>(new ArgumentOutOfRangeException(nameof(thumbnail),
                $"Required thumbnail size {expected}, got {thumbnail.Length}"));

        using var pipeline = MagicImageProcessor.BuildPipeline(imagePath, _processImageSettings.GetOrAdd(
            Vector2.Create(width, height), _ => new ProcessImageSettings
            {
                Width = width, Height = height, ColorProfileMode = ColorProfileMode.ConvertToSrgb,
                ResizeMode = CropScaleMode.Stretch, OrientationMode = OrientationMode.Normalize,
                HybridMode = HybridScaleMode.FavorSpeed
            }));

        return GenerateThumbnail(pipeline, width, height, colorSpace, inPolarCoordinates, thumbnail);
    }

    public unsafe Result<bool> GenerateThumbnail<T>(ProcessedImage image, int width, int height, ColorSpace colorSpace,
        bool inPolarCoordinates, Span<T> thumbnail)
        where T : struct, IBinaryFloatingPointIeee754<T>, IConvertible
    {
        using var stream = new UnmanagedMemoryStream((byte*)image.DataPointer.ToPointer(), image.DataSize);

        using var pipeline = MagicImageProcessor.BuildPipeline(stream, _processImageSettings.GetOrAdd(
            Vector2.Create(width, height), _ => new ProcessImageSettings
            {
                Width = width, Height = height, ColorProfileMode = ColorProfileMode.ConvertToSrgb,
                ResizeMode = CropScaleMode.Stretch, OrientationMode = OrientationMode.Normalize,
                HybridMode = HybridScaleMode.FavorSpeed
            }));

        return GenerateThumbnail(pipeline, width, height, colorSpace, inPolarCoordinates, thumbnail);
    }

    private Result<bool> GenerateThumbnail<T>(ProcessingPipeline imageProcessingPipeline, int width, int height,
        ColorSpace colorSpace, bool inPolarCoordinates, Span<T> thumbnail)
        where T : struct, IBinaryFloatingPointIeee754<T>, IConvertible
    {
        try
        {
            var stride = Unsafe.As<ColorSpace, int>(ref colorSpace) * width;
            if (colorSpace is ColorSpace.Grayscale)
            {
                imageProcessingPipeline = imageProcessingPipeline.AddTransform(GreyPixelTransform);
            }
            else
            {
                imageProcessingPipeline = imageProcessingPipeline.AddTransform(BgrPixelTransform);
                imageProcessingPipeline = imageProcessingPipeline.AddTransform(BgrToRgb);
            }

            imageProcessingPipeline = imageProcessingPipeline.AddTransform(GaussianFilter);

            var imageSize = stride * height;
            using var tempImage = MemoryOwner<byte>.Allocate(imageSize);
            imageProcessingPipeline.PixelSource.CopyPixels(
                _thumbnailSizes.GetOrAdd(Vector2.Create(width, height), _ => new Rectangle(0, 0, width, height)),
                stride, tempImage.Span);

            return !inPolarCoordinates
                ? SplitImageAsSpecifiedFloatChannels(tempImage.Span[..imageSize], thumbnail[..imageSize], colorSpace)
                : polarCoordinatesConverter.ConvertToPolarCoordinates(tempImage.Span[..imageSize],
                    thumbnail[..imageSize], width, height, colorSpace);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return new Result<bool>(e);
        }
    }

    private static bool SplitImageAsSpecifiedFloatChannels<T>(ReadOnlySpan<byte> input, Span<T> output,
        ColorSpace colorSpace)  where T : struct, IBinaryFloatingPointIeee754<T>, IConvertible
    {
        if (colorSpace is ColorSpace.Grayscale)
        {
            TensorPrimitives.ConvertChecked(input, output);
            return true;
        }

        // Since MagicScaler use BGR, swap the red and blue channels to get RGB
        var length = input.Length / 3;
        for (var i = 0; i < length; i += 3)
        {
            (output[i], output[length + i], output[2 * length + i]) = (T.CreateChecked(input[i]),
                T.CreateChecked(input[i + 1]), T.CreateChecked(input[i + 2]));
        }

        return true;
    }
}