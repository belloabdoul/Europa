using System.Runtime.InteropServices;
using DotNext.Buffers;
using Microsoft.Win32.SafeHandles;

namespace API.Implementations.Common;

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

    public static unsafe Memory<byte> AsMemory(IntPtr bufferPointer, int bufferSize)
    {
        return UnmanagedMemory.AsMemory((byte*)bufferPointer, bufferSize);
    }

    public static Memory<byte>[] GetPageAlignedMemoryList(Memory<byte> buffer)
    {
        var memoryListCount = buffer.Length / Environment.SystemPageSize;

        var buffers = new Memory<byte>[memoryListCount];

        for (var i = 0; i < memoryListCount; i++)
            buffers[i] = buffer.Slice(i * Environment.SystemPageSize, Environment.SystemPageSize);

        return buffers;
    }

    public static unsafe void FreeAlignedMemory(IntPtr pointer)
    {
        NativeMemory.AlignedFree(pointer.ToPointer());
    }
}