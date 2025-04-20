using System.Numerics;

namespace KFR.Interfaces;

public interface IKfrFactory
{
    /// <summary>
    /// Creates a FFT instance with the specified size.
    /// </summary>
    /// <returns></returns>
    IFft<T> CreateFftInstance<T>(int rows, int columns) where T : struct, ISignedNumber<T>;
    
    /// <summary>
    /// Creates a FCT instance with the specified size.
    /// </summary>
    /// <returns></returns>
    IFct<T> CreateFctInstance<T>(int size) where T : struct, IFloatingPointIeee754<T>;
}