using API.Common.Entities;
using API.Interfaces.SimilarImages;
using OpenCvSharp;
using SkiaSharp;
using File = System.IO.File;

namespace API.Implementations.SimilarImages
{
    public class ImageHashGenerator : IImageHashGenerator
    {
        public string GenerateImageHash(string path, string type)
        {
            var hashString = string.Empty;
            if (type.Contains("image"))
            {
                Mat image;

                // Convert the image's pixels to an OpenCV matrixe and initialize the reference and hash matrixes
                if (type.Contains("gif"))
                {
                    // SkiaSharp
                    using var bitmap = SKBitmap.Decode(path);
                    image = Mat.FromImageData(bitmap.Encode(SKEncodedImageFormat.Webp, 100).ToArray());
                }
                else
                {
                    using var stream = File.OpenRead(path);
                    image = Mat.FromStream(stream, ImreadModes.Color);
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
            }
            return hashString;
        }
    }
}
