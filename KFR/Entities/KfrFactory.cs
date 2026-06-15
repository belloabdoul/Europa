using System.Numerics;
using KFR.Entities.Fct;
using KFR.Entities.Fft;
using KFR.Interfaces;

namespace KFR.Entities;

public class KfrFactory : IKfrFactory
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

    public IFct<T> CreateFctInstance<T>(int size) where T : struct, IFloatingPointIeee754<T>
    {
        if (typeof(T) == typeof(float))
            return (new FloatFct(size) as IFct<T>)!;
        if (typeof(T) == typeof(double))
            return (new DoubleFct(size) as IFct<T>)!;
        throw new NotImplementedException(typeof(T).ToString());
    }
}