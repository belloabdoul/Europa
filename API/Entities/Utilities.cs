using OpenCvSharp.ImgHash;
using System.Collections.Concurrent;
using System.Text;

namespace API.Common.Entities
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

        public static Task ParallelForEachAsync<T>(this IEnumerable<T> source, Func<T, Task> funcBody, int maxDoP = 4)
        {
            async Task AwaitPartition(IEnumerator<T> partition)
            {
                using (partition)
                {
                    while (partition.MoveNext())
                    {
                        await Task.Yield(); // prevents a sync/hot thread hangup
                        await funcBody(partition.Current);
                    }
                }
            }

            return Task.WhenAll(
                Partitioner
                    .Create(source)
                    .GetPartitions(maxDoP)
                    .AsParallel()
                    .Select(AwaitPartition));
        }
    }
}
