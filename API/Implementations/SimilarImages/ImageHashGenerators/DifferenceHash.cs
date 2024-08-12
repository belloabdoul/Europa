using Core.Interfaces;

namespace API.Implementations.SimilarImages.ImageHashGenerators;

public class DifferenceHash : IImageHash
{
    private const int Width = 9;
    private const int Height = 8;
    public int GetRequiredWidth() => Width;
    public int GetRequiredHeight() => Height;

    public byte[] GenerateHash(byte[] pixels)
    {
            var hash = new byte[(Width - 1) * Height];
            
            var pixelNewLine = 0;
            for (var i = 0; i < pixels.Length; i++)
            {
                if ((i + 1) % Width == 0)
                    pixelNewLine++;
                else if (pixels[i] < pixels[i + 1])
                    hash[i - pixelNewLine] = 1;
            }
            
            return hash;
        
    }
} 