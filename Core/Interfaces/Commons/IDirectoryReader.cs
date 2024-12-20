﻿using System.Buffers;
using Core.Entities.SearchParameters;

namespace Core.Interfaces.Commons;

public interface IDirectoryReader
{
    Task<string[]> GetAllFilesFromFolderAsync(SearchParameters searchParameters, CancellationToken cancellationToken);

    IEnumerable<string> GetFilesInFolder(string folder, long? minSize, long? maxSize, SearchValues<string> includedFileTypes,
        SearchValues<string> excludedFileTypes, bool includeSubFolders = false);
}