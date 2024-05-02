using OpenCvSharp.ImgHash;

namespace Core.Entities
{
    public static class Utilities
    {
        public static void GetHashFunction(out ImgHashBase blockMeanHash)
        {
            blockMeanHash = BlockMeanHash.Create(BlockMeanHashMode.Mode1);
        }
    }
}