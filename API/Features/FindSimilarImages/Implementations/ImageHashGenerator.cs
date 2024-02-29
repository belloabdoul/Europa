using API.Common.Entities;
using API.Features.FindSimilarImages.Interfaces;
using ImageMagick;
using OpenCvSharp;
using SkiaSharp;
using System.Collections.Concurrent;
using System.Reflection.Metadata.Ecma335;
using File = System.IO.File;

namespace API.Features.FindSimilarImages.Implementations
{
    public class ImageHashGenerator : IImageHashGenerator
    {
        public List<string> GenerateAnimatedImageHashes(string path, string type)
        {
            throw new NotImplementedException();
        }

        public string GenerateImageHash(string path, string type)
        {
            var hashString = string.Empty;
            if (type.Contains("image"))
            {
                Mat image;
                if (type.Contains("gif"))
                {
                    // Magick.NET
                    //using var magickImage = new MagickImage(path);
                    //magickImage.Format = MagickFormat.WebP;
                    //magickImage.Quality = 100;
                    //using var stream = new MemoryStream();
                    //magickImage.Write(stream);
                    //image = Mat.FromImageData(magickImage.ToByteArray(), ImreadModes.Color);

                    // SkiaSharp
                    using var bitmap = SKBitmap.Decode(path);
                    image = Mat.FromImageData(bitmap.Encode(SKEncodedImageFormat.Webp, 100).ToArray());
                }
                else
                {
                    using var stream = File.OpenRead(path);
                    image = Mat.FromStream(stream, ImreadModes.Color);
                }

                // Convert the image's pixels to an OpenCV matrixe and initialize the reference and hash matrixes
                using var hash = new Mat<byte>();
                //using var reference = Mat.Zeros(1, 121, MatType.CV_8UC1).ToMat();

                // Initialize the block mean hash function
                Utilities.GetHashFunction(out var blockMeanHash);

                using (blockMeanHash)
                {
                    // Generate perceptual hash and hamming distance from reference and save in image hash
                    blockMeanHash.Compute(image, hash);
                    //hashString = Convert.ToHexString(hash.ToArray());
                    hashString = string.Join(string.Empty, hash.ToArray().Select(val => Convert.ToString(val, 2).PadLeft(8, '0')));
                    //var distance = blockMeanHash.Compare(hash, reference);

                    //imageHashes[fileName] = (hash.ToArray(), distance);
                }

                image.Dispose();
            }
            return hashString;
        }
    }
}
