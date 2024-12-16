using System.Collections;

namespace Api.Implementations.SimilarAudios.MinHash
{
    internal interface IMinHashService<out T>
    {
        /// <summary>
        ///  Hash input array using N hash functions
        /// </summary>
        /// <param name="fingerprint">Fingerprint signature to hash</param>
        /// <param name="n">Number of hash functions to use</param>
        /// <returns>Min-hashed fingerprint, of size N</returns>
        T[] Hash(BitArray fingerprint, int n);
    }
}