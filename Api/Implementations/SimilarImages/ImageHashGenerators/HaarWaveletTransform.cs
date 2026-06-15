using System.Runtime.CompilerServices;

namespace Api.Implementations.SimilarImages.ImageHashGenerators;

public static class HaarWaveletTransform
{
    public static void DecomposeImageInPlace(Span<double> image, int rows, int cols, double waveletNorm)
    {
        DecomposeImage(image, rows, cols, waveletNorm);
    }

    [SkipLocalsInit]
    private static void DecomposeImage(Span<double> image, int rows, int cols, double waveletNorm)
    {
        Span<double> temp = stackalloc double[rows > cols ? rows : cols];
        Span<double> column = stackalloc double[rows]; /*Length of each column is equal to number of rows*/

        // The order of decomposition is reversed because the image is 128x32 but we consider it reversed 32x128
        for (var col = 0; col < cols /*32*/; col++)
        {
            for (var colIndex = 0; colIndex < rows; colIndex++)
            {
                column[colIndex] = image[col + colIndex * cols]; /*Copying Column vector*/
            }

            DecompositionArray(column, temp, waveletNorm); /*Decomposition of each row*/
            for (var colIndex = 0; colIndex < rows; colIndex++)
            {
                image[col + cols * colIndex] = column[colIndex];
            }
        }

        for (var row = 0; row < rows /*128*/; row++)
        {
            DecompositionRow(image, row, cols, temp, waveletNorm); /*Decomposition of each row*/
        }
    }

    private static void DecompositionArray(Span<double> array, Span<double> temp, double waveletNorm)
    {
        var h = array.Length;
        while (h > 1)
        {
            DecompositionStep(array, h, 0, temp, waveletNorm);
            h /= 2;
        }
    }

    private static void DecompositionRow(Span<double> array, int row, int cols, Span<double> temp, double waveletNorm)
    {
        var h = cols;
        while (h > 1)
        {
            DecompositionStep(array, h, row * cols, temp, waveletNorm);
            h /= 2;
        }
    }

    private static void DecompositionStep(Span<double> array, int h, int prefix, Span<double> temp, double waveletNorm)
    {
        h /= 2;
        for (int i = 0, j = 0; i < h; ++i, j = 2 * i)
        {
            temp[i] = (float)((array[prefix + j] + array[prefix + j + 1]) / waveletNorm);
            temp[i + h] = (float)((array[prefix + j] - array[prefix + j + 1]) / waveletNorm);
        }

        temp[..(h * 2)].CopyTo(array[prefix..]);
    }
}