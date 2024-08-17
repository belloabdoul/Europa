using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Pipelines.Sockets.Unofficial;

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
    
    public static UnmanagedMemoryManager<byte> GetUnmanagedMemoryManager(IntPtr bufferPointer, int bufferSize)
    {
        return new UnmanagedMemoryManager<byte>(bufferPointer, bufferSize);
    }
    
    public static Memory<byte>[] GetPageAlignedMemoryList(Memory<byte> buffer)
    {
        var memoryListCount = buffer.Length / Environment.SystemPageSize;
        var memoryList = new Memory<byte>[memoryListCount];
        for (var i = 0; i < memoryListCount; i++)
            memoryList[i] = buffer.Slice(i * Environment.SystemPageSize, Environment.SystemPageSize);

        return memoryList;
    }
    
    public static unsafe void FreeAlignedMemory(IntPtr pointer)
    {
        NativeMemory.AlignedFree(pointer.ToPointer());
    }
}