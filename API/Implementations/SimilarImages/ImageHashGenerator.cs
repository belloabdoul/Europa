using System.Runtime.InteropServices;
using Core.Entities;
using Core.Interfaces.SimilarImages;
using Emgu.CV;
using Emgu.CV.CvEnum;
using NoAlloq;
using Pgvector;
using SkiaSharp;

namespace API.Implementations.SimilarImages;

public class ImageHashGenerator : IImageHashGenerator
{
    public Vector GenerateImageHash(string path)
    {
        float[] hashAsEmbeddings;

        Utilities.GetHashFunction(out var blockMeanHash);

        // If on Windows remove the 260 characters limitation
        using (blockMeanHash)
        using (var image =
               RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                   ? SKBitmap.Decode(Utilities.GetValidPath(path))
                   : SKBitmap.Decode(path))
        using (var mat = new Mat(image.Height, image.Width, DepthType.Cv8U, image.BytesPerPixel,
                   image.GetPixels(), image.RowBytes))
        using (var hash = new Mat())
        {
            blockMeanHash.Compute(mat, hash);

            hashAsEmbeddings = hash.GetSpan<byte>().Select(byteValue => Utilities.ByteToNormalized[byteValue])
                .ToArray();
        }

        return new Vector(hashAsEmbeddings);
    }
}