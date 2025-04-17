using System.Numerics;
using System.Runtime.InteropServices;
using KFR.Interop;

namespace KFR.Entities;

public sealed class KfrFft<T>(int rows, int columns)
    : IDisposable where T : struct
{
    private nuint _plan = typeof(T) == typeof(float)
        ? rows == 1
            ? Interop64.kfr_dft_real_create_plan_f32(columns, DftPackFormat.Perm)
            : Interop64.kfr_dft_real_create_2d_plan_f32(rows, columns, false)
        : typeof(T) == typeof(double)
            ? rows == 1
                ? Interop64.kfr_dft_real_create_plan_f64(columns, DftPackFormat.Perm)
                : throw new NotSupportedException("2D real to complex DFT not supported")
            : rows == 1
                ? Interop64.kfr_dft_create_plan_f64(columns, DftPackFormat.Perm)
                : Interop64.kfr_dft_create_2d_plan_f64(rows, columns, DftPackFormat.Perm);

    public nint Rows { get; } = rows;
    public nint Columns { get; } = columns;
    public nint Size { get; } = rows * columns;
    public nint SizeToAllocate => Interop64.kfr_allocated_size(_plan);


    public void DumpPlanInformation()
    {
        if (typeof(T) == typeof(float))
            Interop64.kfr_dft_real_dump_f32(_plan);
        else if (typeof(T) == typeof(double))
            Interop64.kfr_dft_real_dump_f64(_plan);
        else if (typeof(T) == typeof(Complex))
            Interop64.kfr_dft_dump_f64(_plan);
        else
            throw new NotImplementedException("No FFT implementation exist for requested data type");
    }

    public int GetTempBufferSize()
    {
        if (typeof(T) == typeof(float))
            return Convert.ToInt32(Interop64.kfr_dft_real_get_temp_size_f32(_plan));
        if (typeof(T) == typeof(double))
            return Convert.ToInt32(Interop64.kfr_dft_real_get_temp_size_f64(_plan));
        if (typeof(T) == typeof(Complex))
            return Convert.ToInt32(Interop64.kfr_dft_get_temp_size_f64(_plan));

        throw new NotImplementedException("No FFT implementation exist for requested data type");
    }

    public void Forward(Span<T> inPlace, Span<byte> temp)
    {
        if (typeof(T) == typeof(float))
            Interop64.kfr_dft_real_execute_f32(_plan, MemoryMarshal.Cast<T, float>(inPlace),
                MemoryMarshal.Cast<T, float>(inPlace), temp);
        else
        {
            var doubleInPlace = MemoryMarshal.Cast<T, double>(inPlace);
            if (typeof(T) == typeof(double))
                Interop64.kfr_dft_real_execute_f64(_plan, doubleInPlace, doubleInPlace, temp);
            else if (typeof(T) == typeof(Complex))
                Interop64.kfr_dft_execute_f64(_plan, doubleInPlace, doubleInPlace, temp);
            else
                throw new NotImplementedException("No FFT implementation exist for requested data type");
        }
    }


    public void Forward(Span<T> input, Span<T> output, Span<byte> temp)
    {
        if (typeof(T) == typeof(float))
            Interop64.kfr_dft_real_execute_f32(_plan, MemoryMarshal.Cast<T, float>(output),
                MemoryMarshal.Cast<T, float>(input), temp);
        else
        {
            var doubleInput = MemoryMarshal.Cast<T, double>(input);
            var doubleOutput = MemoryMarshal.Cast<T, double>(output);
            if (typeof(T) == typeof(double))
                Interop64.kfr_dft_real_execute_f64(_plan, doubleOutput, doubleInput, temp);
            else if (typeof(T) == typeof(Complex))
                Interop64.kfr_dft_execute_f64(_plan, doubleOutput, doubleInput, temp);
            else
                throw new NotImplementedException("No FFT implementation exist for requested data type");
        }
    }

    public void Inverse(Span<T> inPlace, Span<byte> temp)
    {
        if (typeof(T) == typeof(float))
            Interop64.kfr_dft_real_execute_inverse_f32(_plan, MemoryMarshal.Cast<T, float>(inPlace),
                MemoryMarshal.Cast<T, float>(inPlace), temp);
        else
        {
            var doubleInPlace = MemoryMarshal.Cast<T, double>(inPlace);
            if (typeof(T) == typeof(double))
                Interop64.kfr_dft_real_execute_inverse_f64(_plan, doubleInPlace, doubleInPlace, temp);
            else if (typeof(T) == typeof(Complex))
                Interop64.kfr_dft_execute_inverse_f64(_plan, doubleInPlace, doubleInPlace, temp);
            else
                throw new NotImplementedException("No FFT implementation exist for requested data type");
        }
    }

    public void Inverse(Span<T> input, Span<T> output, Span<byte> temp)
    {
        if (typeof(T) == typeof(float))
            Interop64.kfr_dft_real_execute_inverse_f32(_plan, MemoryMarshal.Cast<T, float>(output),
                MemoryMarshal.Cast<T, float>(input), temp);
        else
        {
            var doubleInput = MemoryMarshal.Cast<T, double>(input);
            var doubleOutput = MemoryMarshal.Cast<T, double>(output);
            if (typeof(T) == typeof(double))
                Interop64.kfr_dft_real_execute_inverse_f64(_plan, doubleOutput, doubleInput, temp);
            else if (typeof(T) == typeof(Complex))
                Interop64.kfr_dft_execute_inverse_f64(_plan, doubleOutput, doubleInput, temp);
            else
                throw new NotImplementedException("No FFT implementation exist for requested data type");
        }
    }

    public void Dispose()
    {
        if (_plan == UIntPtr.Zero)
            return;

        if (typeof(T) == typeof(float))
            Interop64.kfr_dft_real_delete_plan_f32(_plan);
        if (typeof(T) == typeof(double))
            Interop64.kfr_dft_real_delete_plan_f64(_plan);
        if (typeof(T) == typeof(Complex))
            Interop64.kfr_dft_delete_plan_f64(_plan);
        _plan = UIntPtr.Zero;
    }
}