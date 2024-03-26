using OpenCvSharp.ImgHash;
using System.Text;

namespace Core.Entities
{
    public static class Utilities
    {
        public static void GetHashFunction(out BlockMeanHash blockMeanHash)
        {
            blockMeanHash = BlockMeanHash.Create(BlockMeanHashMode.Mode1);
        }

        public static string BinaryStringToHexString(string binary)
        {
            var result = new StringBuilder(binary.Length / 8 + 1);

            for (int i = 0; i < binary.Length; i += 8)
            {
                string eightBits = binary.Substring(i, 8);
                result.AppendFormat("{0:X2}", Convert.ToByte(eightBits, 2));
            }

            return result.ToString();
        }
    }
}
