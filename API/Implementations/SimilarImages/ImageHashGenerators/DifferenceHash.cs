using Core.Interfaces;
using DotNext.Buffers;
using NetVips;

namespace API.Implementations.SimilarImages.ImageHashGenerators;

public class DifferenceHash : IImageHash
{
    private const int Height = 8;
    private const int Width = 9;

    public unsafe byte[] GenerateHash(string path)
    {
        try
        {
            using var resizedImage = Image.Thumbnail(path, Width, Height, Enums.Size.Force);
            using var grayscaleImage = resizedImage.Colourspace(Enums.Interpretation.Bw);
            using var imageWithoutAlpha = grayscaleImage.Flatten();

            var pointer = imageWithoutAlpha.WriteToMemory(out _);
            var pixels = UnmanagedMemory.AsMemory((byte*)pointer.ToPointer(), Width * Height);

            var pixelNewLine = 0;
            var hash = new byte[(Width - 1) * Height];
            for (var i = 0; i < pixels.Length; i++)
            {
                if ((i + 1) % Width == 0)
                    pixelNewLine++;
                else if (pixels.Span[i] < pixels.Span[i + 1])
                    hash[i - pixelNewLine] = 1;
            }

            NetVips.NetVips.Free(pointer);
            return hash;
        }
        catch (Exception)
        {
            return [];
        }
    }
}