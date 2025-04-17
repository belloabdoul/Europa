using System.Numerics;
using KFR.Interfaces;

namespace KFR.Entities;

public class FftFactory : IFftFactory
{
    public IFft<T> CreateFftInstance<T>(int rows, int columns)
        where T : struct, ISignedNumber<T>
    {
        if (typeof(T) == typeof(float))
            return (new FloatFft(rows, columns) as IFft<T>)!;
        if (typeof(T) == typeof(double))
            return (new DoubleFft(rows, columns) as IFft<T>)!;
        if (typeof(T) == typeof(Complex))
            return (new ComplexFft(rows, columns) as IFft<T>)!;
        throw new NotImplementedException(typeof(T).ToString());
    }
}