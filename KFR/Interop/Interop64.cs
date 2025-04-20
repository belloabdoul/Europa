using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using KFR.Entities;

namespace KFR.Interop;

internal static partial class Interop64
{
    private const string KfrLib = "kfr_capi";

    [LibraryImport(KfrLib, EntryPoint = "kfr_allocated_size")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint kfr_allocated_size(nuint plan);

    #region Float DCT

    [LibraryImport(KfrLib, EntryPoint = "kfr_dct_create_plan_f32")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nuint kfr_dct_create_plan_f32(nint size);

    [LibraryImport(KfrLib, EntryPoint = "kfr_dct_delete_plan_f32")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void kfr_dct_delete_plan_f32(nuint plan);

    [LibraryImport(KfrLib, EntryPoint = "kfr_dct_dump_f32")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void kfr_dct_dump_f32(nuint plan);

    [LibraryImport(KfrLib, EntryPoint = "kfr_dct_execute_f32")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void
        kfr_dct_execute_f32(nuint plan, Span<float> output, Span<float> input, Span<byte> temp);

    [LibraryImport(KfrLib, EntryPoint = "kfr_dct_execute_inverse_f32")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void kfr_dct_execute_inverse_f32(nuint plan, Span<float> output, Span<float> input,
        Span<byte> temp);

    [LibraryImport(KfrLib, EntryPoint = "kfr_dct_get_temp_size_f32")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint kfr_dct_get_temp_size_f32(nuint plan);

    #endregion

    #region Double DCT

    [LibraryImport(KfrLib, EntryPoint = "kfr_dct_create_plan_f64")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nuint kfr_dct_create_plan_f64(nint size);

    [LibraryImport(KfrLib, EntryPoint = "kfr_dct_delete_plan_f64")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void kfr_dct_delete_plan_f64(nuint plan);

    [LibraryImport(KfrLib, EntryPoint = "kfr_dct_dump_f64")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void kfr_dct_dump_f64(nuint plan);

    [LibraryImport(KfrLib, EntryPoint = "kfr_dct_execute_f64")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void
        kfr_dct_execute_f64(nuint plan, Span<double> output, Span<double> input, Span<byte> temp);

    [LibraryImport(KfrLib, EntryPoint = "kfr_dct_execute_inverse_f64")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void kfr_dct_execute_inverse_f64(nuint plan, Span<double> output, Span<double> input,
        Span<byte> temp);

    [LibraryImport(KfrLib, EntryPoint = "kfr_dct_get_temp_size_f64")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint kfr_dct_get_temp_size_f64(nuint plan);

    #endregion

    #region Float DFT common operations

    [LibraryImport(KfrLib, EntryPoint = "kfr_dft_real_delete_plan_f32")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void kfr_dft_real_delete_plan_f32(nuint plan);

    [LibraryImport(KfrLib, EntryPoint = "kfr_dft_real_dump_f32")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void kfr_dft_real_dump_f32(nuint plan);

    [LibraryImport(KfrLib, EntryPoint = "kfr_dft_real_get_temp_size_f32")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nuint kfr_dft_real_get_temp_size_f32(nuint plan);

    [LibraryImport(KfrLib, EntryPoint = "kfr_dft_real_execute_f32")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void kfr_dft_real_execute_f32(nuint plan, Span<float> output, ReadOnlySpan<float> input,
        Span<byte> temp);

    [LibraryImport(KfrLib, EntryPoint = "kfr_dft_real_execute_inverse_f32")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void kfr_dft_real_execute_inverse_f32(nuint plan, Span<float> output,
        ReadOnlySpan<float> input, Span<byte> temp);

    #endregion

    #region Float DFT 1D and 2D plans

    [LibraryImport(KfrLib, EntryPoint = "kfr_dft_real_create_plan_f32")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nuint kfr_dft_real_create_plan_f32(nint size, DftPackFormat dftPackFormat);

    [LibraryImport(KfrLib, EntryPoint = "kfr_dft_real_create_2d_plan_f32")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nuint kfr_dft_real_create_2d_plan_f32(nint rows, nint cols,
        [MarshalAs(UnmanagedType.I1)] bool isEnough);

    #endregion

    #region DFT 1D Double

    [LibraryImport(KfrLib, EntryPoint = "kfr_dft_real_create_plan_f64")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nuint kfr_dft_real_create_plan_f64(nint size, DftPackFormat dftPackFormat);

    [LibraryImport(KfrLib, EntryPoint = "kfr_dft_real_delete_plan_f64")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void kfr_dft_real_delete_plan_f64(nuint plan);

    [LibraryImport(KfrLib, EntryPoint = "kfr_dft_real_dump_f64")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void kfr_dft_real_dump_f64(nuint plan);

    [LibraryImport(KfrLib, EntryPoint = "kfr_dft_real_get_temp_size_f64")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint kfr_dft_real_get_temp_size_f64(nuint plan);

    [LibraryImport(KfrLib, EntryPoint = "kfr_dft_real_execute_f64")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void kfr_dft_real_execute_f64(nuint plan, Span<double> output, ReadOnlySpan<double> input,
        Span<byte> temp);

    [LibraryImport(KfrLib, EntryPoint = "kfr_dft_real_execute_inverse_f64")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void kfr_dft_real_execute_inverse_f64(nuint plan, Span<double> output,
        ReadOnlySpan<double> input,
        Span<byte> temp);

    #endregion

    #region DFT 1D Complex

    [LibraryImport(KfrLib, EntryPoint = "kfr_dft_create_plan_f64")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nuint kfr_dft_create_plan_f64(nint size, DftPackFormat dftPackFormat);

    [LibraryImport(KfrLib, EntryPoint = "kfr_dft_delete_plan_f64")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void kfr_dft_delete_plan_f64(nuint plan);

    [LibraryImport(KfrLib, EntryPoint = "kfr_dft_dump_f64")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void kfr_dft_dump_f64(nuint plan);

    [LibraryImport(KfrLib, EntryPoint = "kfr_dft_get_temp_size_f64")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nint kfr_dft_get_temp_size_f64(nuint plan);

    [LibraryImport(KfrLib, EntryPoint = "kfr_dft_execute_f64")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void kfr_dft_execute_f64(nuint plan, Span<double> output, ReadOnlySpan<double> input,
        Span<byte> temp);

    [LibraryImport(KfrLib, EntryPoint = "kfr_dft_execute_inverse_f64")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void kfr_dft_execute_inverse_f64(nuint plan, Span<double> output,
        ReadOnlySpan<double> input,
        Span<byte> temp);

    #endregion

    #region DFT 2D Complex

    [LibraryImport(KfrLib, EntryPoint = "kfr_dft_create_2d_plan_f64")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial nuint kfr_dft_create_2d_plan_f64(nint rows, nint columns, DftPackFormat dftPackFormat);

    #endregion
}