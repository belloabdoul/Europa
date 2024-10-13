using System.Numerics;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Core.Entities;
using Core.Interfaces;
using Core.Interfaces.Common;
using NetVips;
using Sdcb.LibRaw;

namespace Api.Implementations.SimilarImages.ImageProcessors;

public class LibVipsImageProcessor : IFileTypeIdentifier, IThumbnailGenerator, IMainThumbnailGenerator
{
    public FileSearchType AssociatedSearchType => FileSearchType.Images;

    public FileType AssociatedImageType => FileType.LibVipsImage;

    private static readonly Vector3 GrayscaleCoefficients = new(0.0722f, 0.7152f, 0.2126f);

    public FileType GetFileType(string path)
    {
        try
        {
            // Return if the image is a large image or not depending on if its uncompressed size is bigger than 10Mb
            using var image = Image.NewFromFile(path, false, Enums.Access.Sequential,
                Enums.FailOn.Error);
            var loader = (string)image.Get("vips-loader");
            if (loader.Contains("gif", StringComparison.InvariantCultureIgnoreCase) ||
                loader.Contains("webp", StringComparison.InvariantCultureIgnoreCase) ||
                loader.Contains("png", StringComparison.InvariantCultureIgnoreCase))
                return image.GetFields().Contains("n-pages", StringComparer.InvariantCultureIgnoreCase)
                    ? FileType.Animation
                    : FileType.LibVipsImage;
            return FileType.LibVipsImage;
        }
        catch (VipsException)
        {
            return FileType.CorruptUnknownOrUnsupported;
        }
    }

    public ValueTask<bool> GenerateThumbnail<T>(ProcessedImage image, int width, int height, Span<T> pixels)
        where T : INumberBase<T>
    {
        var pointer = IntPtr.Zero;
        var imageSize = width * height;
        var done = false;
        try
        {
            using var imageFromBuffer = Image.NewFromMemory(image.DataPointer, Convert.ToUInt64(image.DataSize),
                image.Width, image.Height, image.Channels, Enums.BandFormat.Uchar);
            using var resizedImage = imageFromBuffer.ThumbnailImage(width, height, Enums.Size.Force);
            using var grayscaleImage = resizedImage.Colourspace(Enums.Interpretation.Srgb);
            using var imageWithoutAlpha = grayscaleImage.Flatten();
            pointer = imageWithoutAlpha.WriteToMemory(out var size);

            var length = Convert.ToInt32(size);

            ReadOnlySpan<byte> providedPixels;
            unsafe
            {
                providedPixels = new Span<byte>(pointer.ToPointer(), length);
            }

            if (length == imageSize)
            {
                if (typeof(T) == typeof(byte))
                    providedPixels.CopyTo(
                        MemoryMarshal.CreateSpan(ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(pixels)),
                            length));
                else
                    TensorPrimitives.ConvertChecked(providedPixels, pixels);
            }
            else
            {
                Span<float> tempPixelsFloat = stackalloc float[length];

                TensorPrimitives.ConvertChecked(providedPixels, tempPixelsFloat);

                var pixelsAsVector3 = MemoryMarshal.CreateReadOnlySpan(
                    ref Unsafe.As<float, Vector3>(ref MemoryMarshal.GetReference(tempPixelsFloat)),
                    length);

                ref var pixelsRef = ref MemoryMarshal.GetReference(pixels);
                ref var pixelsAsVector3Ref = ref MemoryMarshal.GetReference(pixelsAsVector3);

                for (nuint i = 0; i < Convert.ToUInt64(imageSize); i++)
                {
                    Unsafe.Add(ref pixelsRef, i) =
                        T.CreateChecked(MathF.Ceiling(Vector3.Dot(Unsafe.Add(ref pixelsAsVector3Ref, i),
                            GrayscaleCoefficients)));
                }
            }

            done = true;
        }
        catch (Exception)
        {
        }
        finally
        {
            if (pointer != IntPtr.Zero)
                NetVips.NetVips.Free(pointer);
        }

        return ValueTask.FromResult(done);
    }

    public ValueTask<bool> GenerateThumbnail<T>(string imagePath, int width, int height, Span<T> pixels)
        where T : INumberBase<T>
    {
        var pointer = IntPtr.Zero;
        var imageSize = width * height;
        var done = false;
        try
        {
            using var resizedImage = Image.Thumbnail(imagePath, width, height, Enums.Size.Force);
            using var grayscaleImage = resizedImage.Colourspace(Enums.Interpretation.Srgb);
            using var imageWithoutAlpha = grayscaleImage.HasAlpha() ? grayscaleImage.Flatten() : grayscaleImage;
            pointer = imageWithoutAlpha.WriteToMemory(out var size);

            var length = Convert.ToInt32(size);

            ReadOnlySpan<byte> providedPixels;
            unsafe
            {
                providedPixels = new Span<byte>(pointer.ToPointer(), length);
            }

            if (length == imageSize)
                TensorPrimitives.ConvertChecked(providedPixels, pixels);
            else
            {
                Span<float> tempPixelsFloat = stackalloc float[length];

                TensorPrimitives.ConvertChecked(providedPixels, tempPixelsFloat);

                var pixelsAsVector3 = MemoryMarshal.CreateReadOnlySpan(
                    ref Unsafe.As<float, Vector3>(ref MemoryMarshal.GetReference(tempPixelsFloat)),
                    length);

                ref var pixelsRef = ref MemoryMarshal.GetReference(pixels);
                ref var pixelsAsVector3Ref = ref MemoryMarshal.GetReference(pixelsAsVector3);

                for (nuint i = 0; i < Convert.ToUInt64(imageSize); i++)
                {
                    Unsafe.Add(ref pixelsRef, i) =
                        T.CreateSaturating(MathF.Ceiling(Vector3.Dot(Unsafe.Add(ref pixelsAsVector3Ref, i),
                            GrayscaleCoefficients)));
                }
            }

            done = true;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
        finally
        {
            if (pointer != IntPtr.Zero)
                NetVips.NetVips.Free(pointer);
        }

        return ValueTask.FromResult(done);
    }
}