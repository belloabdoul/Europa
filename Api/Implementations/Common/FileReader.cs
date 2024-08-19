using System.Runtime.InteropServices;
using DotNext.Buffers;
using Microsoft.Win32.SafeHandles;

namespace Api.Implementations.Common;

public static class FileReader
{
    public static SafeFileHandle GetFileHandle(string path, bool sequential = false, bool isAsync = false)
    {
        var options = FileOptions.None;
        options |= sequential ? FileOptions.SequentialScan : FileOptions.RandomAccess;
        if (isAsync)
            options |= FileOptions.Asynchronous;
        return File.OpenHandle(path, options: options);
    }

    public static unsafe void* AllocateAlignedMemory(int bufferSize)
    {
        return NativeMemory.AlignedAlloc(Convert.ToUInt32(bufferSize), Convert.ToUInt32(Environment.SystemPageSize));
    }

    public static unsafe Memory<byte> AsMemory(byte* bufferPointer, int bufferSize)
    {
        return UnmanagedMemory.AsMemory(bufferPointer, bufferSize);
    }

    public static unsafe void FreeAlignedMemory(IntPtr pointer)
    {
        NativeMemory.AlignedFree(pointer.ToPointer());
    }
}