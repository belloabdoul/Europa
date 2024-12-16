using System.Collections;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DotNext.Collections.Generic;

namespace Api.Implementations.SimilarAudios.MinHash;

internal class MinHashService : IMinHashService<int>
{
    private readonly IPermutations _permutations;

    internal MinHashService(IPermutations permutations)
    {
        _permutations = permutations;
    }

    /// <summary>
    ///  Gets max entropy permutations.
    /// </summary>
    public static MinHashService MaxEntropy { get; } = new(new MaxEntropyPermutations());

    public int[] Hash(BitArray fingerprint, int n)
    {
        return ComputeMinHashSignature(fingerprint, n);
    }

    /// <summary>
    /// Compute Min Hash signature of a fingerprint
    /// </summary>
    /// <param name="fingerprint">
    /// Fingerprint signature
    /// </param>
    /// <param name="n">
    /// The number of hash functions to use
    /// </param>
    /// <returns>
    /// N-sized sub-fingerprint (length of the permutations number)
    /// </returns>
    /// <remarks>
    /// The basic idea in the Min Hashing scheme is to randomly permute the rows and for each 
    /// column c(i) compute its hash value h(c(i)) as the index of the first row under the permutation that has a 1 in that column.
    /// I.e. http://infolab.stanford.edu/~ullman/mmds/book.pdf s.3.3.4
    /// </remarks>
    [SkipLocalsInit]
    private int[] ComputeMinHashSignature(BitArray fingerprint, int n)
    {
        if (n > _permutations.Count)
        {
            throw new ArgumentException(
                "n should not exceed number of available hash functions: " + _permutations.Count);
        }

        var perms = _permutations.GetPermutations();
        Span<byte> minHash = stackalloc byte[n]; /*100*/
        for (var i = 0; i < n; i++)
        {
            minHash[i] = 255; /*The probability of occurrence of 1 after position 255 is very insignificant*/
            for (byte j = 0; j < perms[i].Length /*256*/; j++)
            {
                var indexAt = perms[i][j];
                if (!fingerprint[indexAt])
                    continue;

                minHash[i] = j; /*Looking for first occurrence of '1'*/
                break;
            }
        }

        return MemoryMarshal.Cast<byte, int>(minHash).ToArray(); /*Array of 100 elements with bit turned ON if permutation captured successfully a TRUE bit*/
    }
}