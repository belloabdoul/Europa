using Core.Entities;
using Core.Interfaces.SimilarImages;
using OpenCvSharp;
using SkiaSharp;

namespace API.Implementations.SimilarImages
{
    public class ImageHashGenerator : IImageHashGenerator
    {
        public string GenerateImageHash(FileStream fileStream, string type)
        {
            var hashString = string.Empty;
            Mat image;

            // Convert the image's pixels to an OpenCV matrixe and initialize the reference and hash matrixes
            if (type.Contains("gif"))
            {
                // SkiaSharp
                using var bitmap = SKBitmap.Decode(fileStream);
                image = Mat.FromImageData(bitmap.Encode(SKEncodedImageFormat.Webp, 100).ToArray());
            }
            else
            {
                image = Mat.FromStream(fileStream, ImreadModes.Color);
            }

            using var hash = new Mat<byte>();

            // Initialize the block mean hash function
            Utilities.GetHashFunction(out var blockMeanHash);

            using (blockMeanHash)
            {
                // Generate perceptual hash
                blockMeanHash.Compute(image, hash);
                hashString = Convert.ToHexString(hash.ToArray());
            }

            image.Dispose();
            return hashString;
        }
    }
}
