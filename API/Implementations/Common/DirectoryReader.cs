using API.Common.Entities;
using API.Interfaces.Common;

namespace API.Implementations.Common
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

            SearchParameters options = new();
            searchParameters.CopyDtoToSearchParametersEntity(options);

            if (options.FileTypeToSearch == FileType.All)
            {
                fileTypes = ["*"];
            }
            if (options.FileTypeToSearch == FileType.Images)
            {
                fileTypes = _imageFormats;
            }
            else if (options.FileTypeToSearch == FileType.Audios)
            {
                fileTypes = _audioFormats;
            }

            if (options.IncludedFileTypes != null && options.IncludedFileTypes.Count != 0)
            {
                if (fileTypes.Contains("*"))
                    fileTypes = [.. options.IncludedFileTypes];
                else
                    fileTypes = options.IncludedFileTypes.Intersect(fileTypes).ToList();
            }

            var maxDegreeOfParallelism = (int)Math.Floor(decimal.Multiply(Environment.ProcessorCount, 0.9m));

            foreach (var folder in folders)
            {
                try
                {
                    if (folder == null || folder.Equals(string.Empty) || !Directory.Exists(folder))
                        errors.Add($"The folder {folder} does not exist.");
                    else
                    {
                        // The search retrieve all files. Depending on user choice
                        // subfolders can be included.
                        // The user can also choose which file extensions to include and is independant from the excluded extension
                        // The user can also choose to exclude extension by default from all searches (like .sys files or .ini files)
                        // or choose an extension to exclude from the current search. THe excluded files will only be used if no
                        // file types to include has been set.
                        token.ThrowIfCancellationRequested();
                        files.AddRange(
                            fileTypes
                            .AsParallel()
                            .WithCancellation(token)
                            .SelectMany(searchPattern => new DirectoryInfo(folder).EnumerateFiles(string.Concat("*.", searchPattern), options.SearchOption))
                            .Where(file => file.Length >= options.MinSize && file.Length <= options.MaxSize).ToList()
                        );

                        files = files.Where(file => !options.ExcludedFileTypes.Any(ext => file.Extension.EndsWith(ext)) && !options.DefaultExcludedFileTypes.Any(ext => file.Extension.EndsWith(ext))).ToList();
                        token.ThrowIfCancellationRequested();
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    errors.Add($"You don't have access the folder {folder}.");
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
