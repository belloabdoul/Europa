using Core.Entities;
using Core.Interfaces.SimilarImages;
using OpenCvSharp;
using Pgvector;
using SkiaSharp;

namespace API.Implementations.SimilarImages;

public class ImageHashGenerator : IImageHashGenerator
{
    public Vector GenerateImageHash(FileStream fileStream)
    {
        float[] hashAsEmbeddings;
        using var image = SKBitmap.Decode(fileStream);
        Utilities.GetHashFunction(out var blockMeanHash);
        
        using (blockMeanHash)
        using (var mat = new Mat(image.Height, image.Width, MatType.CV_8UC(image.BytesPerPixel), image.GetPixels(), image.RowBytes))
        using (var hash = new Mat<byte>())
        {
            blockMeanHash.Compute(mat, hash);
            hash.GetArray(out byte[] hashArray);

            hashAsEmbeddings = new float[hashArray.Length];
            
            for (var i = 0; i < hashArray.Length; i++)
            {
                hashAsEmbeddings[i] = hashArray[i] / 255f;
            }
        }
        
        return new Vector(hashAsEmbeddings);
    }
}