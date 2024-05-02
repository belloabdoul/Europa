using System.Collections.Concurrent;

namespace Core.Entities;

public class FileGroup
{
    public ConcurrentQueue<File> Files { get; }
    public FileGroup(File file)
    {
        Files = [];
        Files.Enqueue(file);
    }
}