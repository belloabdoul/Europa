﻿using API.Common.Entities;
using API.Common.Interfaces;

namespace API.Common.Implementations
{
    public class DirectoryReader : IDirectoryReader
    {
        public bool FileExists(string filePath)
        {
            return System.IO.File.Exists(filePath);
        }

        public string[] GetAllFilesFromFolder(List<string> folders, SearchParametersDto searchParameters, CancellationToken token, out List<string> errors)
        {
            errors = [];
            var files = new List<FileInfo>();

            foreach (var folder in folders)
            {
                try
                {
                    // The search retrieve all files. Depending on user choice
                    // subfolders can be included.
                    // The user can also choose which file extensions to include and is independant from the excluded extension
                    // THe user can also choose to exclude extension by default from all searches (like .sys files or .ini files)
                    // or choose an extension to exclude from the current search. They should cannot be used with included files
                    // to give the user some leeway in case some files types to be excluded need to be checked for.

                    SearchParameters options = new();
                    searchParameters.CopyDtoToSearchParametersEntity(options);

                    token.ThrowIfCancellationRequested();
                    if (options.IncludedFileTypes != null && options.IncludedFileTypes.Count != 0)
                    {
                        files.AddRange(options.IncludedFileTypes
                                                .AsParallel()
                                                .WithCancellation(token)
                                                .SelectMany(searchPattern => new DirectoryInfo(folder).EnumerateFiles(string.Concat("*.", searchPattern), options.SearchOption))
                                                .Where(file => file.Length >= options.MinSize && file.Length <= options.MaxSize));
                    }
                    else
                    {
                        token.ThrowIfCancellationRequested();
                        if (options.ExcludedFileTypes != null && options.ExcludedFileTypes.Count != 0)
                        {
                            files = new DirectoryInfo(folder)
                                .EnumerateFiles("*", options.SearchOption)
                                .Where(file => file.Length >= options.MinSize && file.Length <= options.MaxSize && !options.ExcludedFileTypes.Contains(file.Extension[1..])).ToList();
                        }
                        else
                        {
                            files = new DirectoryInfo(folder)
                                .EnumerateFiles("*", options.SearchOption)
                                .Where(file => file.Length >= options.MinSize && file.Length <= options.MaxSize).ToList();
                        }
                        token.ThrowIfCancellationRequested();
                        // If there are file types to be excluded by default like .sys
                        // files or .inf files, remove them from the list. The user
                        // can also edit this list
                        if (options.DefaultExcludedFileTypes != null && options.DefaultExcludedFileTypes.Count != 0)
                        {
                            //foreach (var file in files) 
                            //{
                            //    Console.WriteLine(file.Extension);
                            //}
                            files = files.Where(file => file.Extension.Length == 0 || !options.DefaultExcludedFileTypes.Contains(file.Extension[1..])).ToList();
                        }
                    }
                    token.ThrowIfCancellationRequested();

                }
                catch (UnauthorizedAccessException)
                {
                    errors.Add($"You don't have the right to access the folder {folder}.");
                }
                catch (Exception ex)
                {
                    errors.Add(ex.Message);
                }
            }
            return files.Select(file => file.FullName).ToArray();
        }
    }
}
