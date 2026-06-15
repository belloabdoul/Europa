using System.Numerics;
using System.Runtime.InteropServices;
using KFR.Interop;

namespace KFR.Entities.Fct;

public class KfrFct<T>(int size) : IDisposable where T : struct, IBinaryFloatingPointIeee754<T>
{
    private nuint _plan = typeof(T) == typeof(float)
        ? Interop64.kfr_dct_create_plan_f32(size)
        : Interop64.kfr_dct_create_plan_f64(size);

    public nint Size { get; } = size;

    public void DumpPlanInformation()
    {
        if (typeof(T) == typeof(float))
            Interop64.kfr_dct_dump_f32(_plan);
        else
            Interop64.kfr_dct_dump_f64(_plan);
        
        throw new NotImplementedException("No FFT implementation exist for requested data type");
    }

    public int GetTempBufferSize()
    {
        if (typeof(T) == typeof(float))
            return Convert.ToInt32(Interop64.kfr_dct_get_temp_size_f32(_plan));
        if (typeof(T) == typeof(double))
            return Convert.ToInt32(Interop64.kfr_dct_get_temp_size_f64(_plan));

        throw new NotImplementedException("No FFT implementation exist for requested data type");
    }

    public void Forward(Span<T> inPlace, Span<byte> temp)
    {
        if (typeof(T) == typeof(float))
            Interop64.kfr_dct_execute_f32(_plan, MemoryMarshal.Cast<T, float>(inPlace),
                MemoryMarshal.Cast<T, float>(inPlace), temp);
        else
        {
            var doubleInPlace = MemoryMarshal.Cast<T, double>(inPlace);
            if (typeof(T) == typeof(double))
                Interop64.kfr_dct_execute_f64(_plan, doubleInPlace, doubleInPlace, temp);
            else
                throw new NotImplementedException("No FFT implementation exist for requested data type");
        }
    }


    public void Forward(Span<T> input, Span<T> output, Span<byte> temp)
    {
        if (typeof(T) == typeof(float))
            Interop64.kfr_dct_execute_f32(_plan, MemoryMarshal.Cast<T, float>(output),
                MemoryMarshal.Cast<T, float>(input), temp);
        else
        {
            var doubleInput = MemoryMarshal.Cast<T, double>(input);
            var doubleOutput = MemoryMarshal.Cast<T, double>(output);
            if (typeof(T) == typeof(double))
                Interop64.kfr_dct_execute_f64(_plan, doubleOutput, doubleInput, temp);
            else
                throw new NotImplementedException("No FFT implementation exist for requested data type");
        }
    }

    public void Inverse(Span<T> inPlace, Span<byte> temp)
    {
        if (typeof(T) == typeof(float))
            Interop64.kfr_dct_execute_inverse_f32(_plan, MemoryMarshal.Cast<T, float>(inPlace),
                MemoryMarshal.Cast<T, float>(inPlace), temp);
        else
        {
            var doubleInPlace = MemoryMarshal.Cast<T, double>(inPlace);
            if (typeof(T) == typeof(double))
                Interop64.kfr_dct_execute_inverse_f64(_plan, doubleInPlace, doubleInPlace, temp);
            else
                throw new NotImplementedException("No FFT implementation exist for requested data type");
        }
    }

    public void Inverse(Span<T> input, Span<T> output, Span<byte> temp)
    {
        if (typeof(T) == typeof(float))
            Interop64.kfr_dct_execute_inverse_f32(_plan, MemoryMarshal.Cast<T, float>(output),
                MemoryMarshal.Cast<T, float>(input), temp);
        else
        {
            var doubleInput = MemoryMarshal.Cast<T, double>(input);
            var doubleOutput = MemoryMarshal.Cast<T, double>(output);
            if (typeof(T) == typeof(double))
                Interop64.kfr_dct_execute_inverse_f64(_plan, doubleOutput, doubleInput, temp);
            else
                throw new NotImplementedException("No FFT implementation exist for requested data type");
        }
    }

    public void Dispose()
    {
        if (_plan == UIntPtr.Zero)
            return;

        if (typeof(T) == typeof(float))
            Interop64.kfr_dct_delete_plan_f32(_plan);
        if (typeof(T) == typeof(double))
            Interop64.kfr_dct_delete_plan_f64(_plan);
        _plan = UIntPtr.Zero;
    }
}