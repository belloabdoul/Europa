﻿namespace Core.Entities.Files;

public class File
{
    // The full path to the file
    public string Path { get; init; }

    // Size of the file
    public long Size { get; init; }

    // The last time the file has been modified
    public DateTime DateModified { get; init; }

    public byte[] Hash { get; init; }

    public File()
    {
        Path = string.Empty;
        Size = 0;
        DateModified = DateTime.MinValue;
        Hash = [];
    }
    
    public File(FileInfo fileInfo)
    {
        Path = fileInfo.FullName;
        Size = fileInfo.Length;
        DateModified = fileInfo.LastWriteTime;
        Hash = [];
    }
}