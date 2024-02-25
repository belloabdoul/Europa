using API.Common.Entities;
using API.Common.Interfaces;
using ImageMagick;
using Microsoft.Extensions.Options;
using SkiaSharp;
using SoundFingerprinting.Audio;
using SoundFingerprinting.Emy;

namespace API.Common.Implementations
{
    public class DirectoryReader : IDirectoryReader
    {
        private readonly List<string> _audioFormats = ["mp3", "flac", "wav", "ogg", "m4a", "aac", "aiff", "pcm", "aif", "aiff", "aifc", "m3a", "mp2", "mp4a", "mp2a", "mpga", "wave", "weba", "wma", "oga"];
        private readonly List<string> _imageFormats = ["mrw", "arw", "srf", "sr2", "mef", "orf", "erf", "kdc", "rw2", "raf", "dcr", "dng", "pef", "crw", "iiq", "nrw", "nef", "cr2", "jpg", "jpeg", "png", 
            "bmp", "tiff", "tif", "tga", "ff", "webp", "gif", "ico", "exr", "qoi", "jpe", "heif", "heic", "avifs"];
        public bool FileExists(string filePath)
        {
            return System.IO.File.Exists(filePath);
        }

        public string[] GetAllFilesFromFolder(List<string> folders, SearchParametersDto searchParameters, CancellationToken token, out List<string> errors)
        {
            errors = [];
            var files = new List<FileInfo>();

            List<string> fileTypes = [];

            if (searchParameters.FilesTypeToSearch == FileType.All)
            {
                fileTypes = ["*"];
            }
            if (searchParameters.FilesTypeToSearch == FileType.Images)
            {
                fileTypes = _imageFormats;
            }
            else if (searchParameters.FilesTypeToSearch == FileType.Audios)
            {
                fileTypes = _audioFormats;
            }

            SearchParameters options = new();
            searchParameters.CopyDtoToSearchParametersEntity(options);

            if (options.IncludedFileTypes != null && options.IncludedFileTypes.Count != 0)
            {
                if (fileTypes.Contains("*"))
                    fileTypes = [.. options.IncludedFileTypes];
                else
                    fileTypes = options.IncludedFileTypes.Intersect(fileTypes).ToList();
            }
            //else
            //{
            //    if (options.ExcludedFileTypes != null && options.ExcludedFileTypes.Count != 0)
            //    {
            //        fileTypes = fileTypes.Except(options.ExcludedFileTypes).ToList();
            //    }
            //    if (options.DefaultExcludedFileTypes != null && options.DefaultExcludedFileTypes.Count != 0)
            //    {
            //        fileTypes = fileTypes.Except(options.DefaultExcludedFileTypes).ToList();
            //    }
            //}

            foreach (var fileType in options.ExcludedFileTypes)
            {
                Console.WriteLine(fileType);
            }

            foreach (var fileType in options.DefaultExcludedFileTypes)
            {
                Console.WriteLine(fileType);
            }

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


                    token.ThrowIfCancellationRequested();
                    if (options.ExcludedFileTypes == null || options.ExcludedFileTypes.Count == 0)
                    {
                        files.AddRange(fileTypes
                            .AsParallel()
                            .WithCancellation(token)
                            .SelectMany(searchPattern => new DirectoryInfo(folder).EnumerateFiles(string.Concat("*.", searchPattern), options.SearchOption))
                            .Where(file => file.Length >= options.MinSize && file.Length <= options.MaxSize));
                    }
                    else
                    {
                        files.AddRange(new DirectoryInfo(folder)
                            .EnumerateFiles("*", options.SearchOption)
                            .Where(file => file.Length >= options.MinSize && file.Length <= options.MaxSize && !options.ExcludedFileTypes.Any(ext => file.Extension.EndsWith(ext) && !options.DefaultExcludedFileTypes.Any(ext => file.Extension.EndsWith(ext)))));
                        //    token.ThrowIfCancellationRequested();
                        //    if (options.ExcludedFileTypes != null && options.ExcludedFileTypes.Count != 0)
                        //    {
                        //        files = new DirectoryInfo(folder)
                        //            .EnumerateFiles("*", options.SearchOption)
                        //            .Where(file => file.Length >= options.MinSize && file.Length <= options.MaxSize && !options.ExcludedFileTypes.Contains(file.Extension[1..])).ToList();
                        //    }
                        //    else
                        //    {
                        //        files = new DirectoryInfo(folder)
                        //            .EnumerateFiles("*", options.SearchOption)
                        //            .Where(file => file.Length >= options.MinSize && file.Length <= options.MaxSize).ToList();
                        //    }
                        //    token.ThrowIfCancellationRequested();
                        //    // If there are file types to be excluded by default like .sys
                        //    // files or .inf files, remove them from the list. The user
                        //    // can also edit this list
                        //    if (options.DefaultExcludedFileTypes != null && options.DefaultExcludedFileTypes.Count != 0)
                        //    {
                        //        //foreach (var file in files) 
                        //        //{
                        //        //    Console.WriteLine(file.Extension);
                        //        //}
                        //        files = files.Where(file => file.Extension.Length == 0 || !options.DefaultExcludedFileTypes.Contains(file.Extension[1..])).ToList();
                        //    }
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
