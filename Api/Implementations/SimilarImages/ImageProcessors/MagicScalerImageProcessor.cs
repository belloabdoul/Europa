using System.Drawing;
using System.Numerics;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
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

    private static readonly IPixelTransform BgrPixelTransform = new FormatConversionTransform(PixelFormats.Bgr24bpp);

    private static readonly ProcessImageSettings ProcessImageSettings = new()
    {
        ColorProfileMode = ColorProfileMode.ConvertToSrgb,
        ResizeMode = CropScaleMode.Stretch, OrientationMode = OrientationMode.Normalize,
        HybridMode = HybridScaleMode.FavorQuality,
    };

    private static readonly Vector3 GrayscaleCoefficients = new(0.0722f, 0.7152f, 0.2126f);

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

    public ValueTask<bool> GenerateThumbnail<T>(string imagePath, int width, int height, Span<T> pixels)
        where T : INumberBase<T>
    {
        if (_area.Width != width || _area.Height != height)
        {
            _area.Width = width;
            _area.Height = height;

            ProcessImageSettings.Width = _area.Width;
            ProcessImageSettings.Height = _area.Height;
        }

        using var pipeline = MagicImageProcessor.BuildPipeline(imagePath, ProcessImageSettings);

        return ValueTask.FromResult(GenerateThumbnail(pipeline, pixels));
    }

    public ValueTask<bool> GenerateThumbnail<T>(ProcessedImage image, int width, int height, Span<T> pixels)
        where T : INumberBase<T>
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

        return ValueTask.FromResult(GenerateThumbnail(pipeline, pixels));
    }

    [SkipLocalsInit]
    private static bool GenerateThumbnail<T>(ProcessingPipeline imageProcessingPipeline, Span<T> pixels)
        where T : INumberBase<T>
    {
        try
        {
            imageProcessingPipeline.AddTransform(BgrPixelTransform);

            var stride = imageProcessingPipeline.PixelSource.Format == PixelFormats.Bgr24bpp
                ? _area.Width * 3
                : _area.Width;

            Span<byte> tempPixels = stackalloc byte[stride * _area.Height];

            imageProcessingPipeline.PixelSource.CopyPixels(_area, stride, tempPixels);

            if (_area.Width == stride)
            {
                TensorPrimitives.ConvertChecked<byte, T>(tempPixels, pixels);
                return true;
            }
            
            Span<float> tempPixelsFloat = stackalloc float[stride * _area.Height];
            TensorPrimitives.ConvertChecked<byte, float>(tempPixels, tempPixelsFloat);

            var imageSize = _area.Width * _area.Height;
            
            var pixelsAsVector3 = MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.As<float, Vector3>(ref MemoryMarshal.GetReference(tempPixelsFloat)),
                imageSize);
            
            ref var pixelsRef = ref MemoryMarshal.GetReference(pixels);
            ref var pixelsAsVector3Ref = ref MemoryMarshal.GetReference(pixelsAsVector3);
            
            for (nuint i = 0; i < Convert.ToUInt64(imageSize); i++)
            {
                Unsafe.Add(ref pixelsRef, i) =
                    T.CreateChecked(MathF.Round(Vector3.Dot(Unsafe.Add(ref pixelsAsVector3Ref, i),
                        GrayscaleCoefficients)));
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}