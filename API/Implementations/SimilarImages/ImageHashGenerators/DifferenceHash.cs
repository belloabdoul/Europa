using Core.Interfaces.SimilarImages;
using NetVips;

namespace API.Implementations.SimilarImages.ImageHashGenerators;

public class DifferenceHash: IImageHash
{
    private const int Height = 8;
    private const int Width = 9;
    
    public byte[] GenerateHash(string path)
    {
        using var resizedImage = Image.Thumbnail(path, Width, Height, Enums.Size.Force);
        using var grayscaleImage = resizedImage.Colourspace(Enums.Interpretation.Bw);
        using var imageWithoutAlpha = grayscaleImage.Flatten();
        var pixels = imageWithoutAlpha.WriteToMemory();
        
        var hash = new byte[Height * (Width - 1)];
        
        var pixelNewLine = 0;
        for (var i = 0; i < pixels.Length; i++)
        {
            if ((i + 1) % Width == 0)
                pixelNewLine++;
            else if(pixels[i] < pixels[i + 1])
                hash[i - pixelNewLine] = 1;
        }
        
        return hash;
    }
}