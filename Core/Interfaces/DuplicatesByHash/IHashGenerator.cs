﻿namespace Core.Interfaces.DuplicatesByHash
{
    public interface IHashGenerator
    {
        string GenerateHash(FileStream fileStream, long lengthToHash);
    }
}
