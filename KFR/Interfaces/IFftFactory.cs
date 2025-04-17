using System.Numerics;
using KFR.Entities;

namespace KFR.Interfaces
{
    public interface IFftFactory
    {
        /// <summary>
        /// Creates a 1D FFT instance with the specified size.
        /// </summary>
        /// <returns></returns>
        IFft<T> CreateFftInstance<T>(int rows, int columns) where T : struct, ISignedNumber<T>;
    }
}
