using Core.Entities;
using Core.Interfaces.Common;
using Microsoft.AspNetCore.SignalR;

namespace API.Implementations.Common
{
    public class DirectoryReader : IDirectoryReader
    {
        private readonly List<string> _audioFormats = ["mp3", "flac", "wav", "ogg", "m4a", "aac", "aiff", "pcm", "aif", "aiff", "aifc", "m3a", "mp2", "mp4a", "mp2a", "mpga", "wave", "weba", "wma", "oga"];
        private readonly List<string> _imageFormats = ["mrw", "arw", "srf", "sr2", "mef", "orf", "erf", "kdc", "rw2", "raf", "dcr", "dng", "pef", "crw", "iiq", "nrw", "nef", "cr2", "jpg", "jpeg", "png",
            "bmp", "tiff", "tif", "tga", "ff", "webp", "gif", "ico", "exr", "qoi", "jpe", "heif", "heic", "avifs"];
        private readonly IHubContext<NotificationHub> _notificationContext;


        public DirectoryReader(IHubContext<NotificationHub> notificationContext) 
        {
            _notificationContext = notificationContext;
        }

        public bool FileExists(string filePath)
        {
            return System.IO.File.Exists(filePath);
        }

        public static bool HasWriteAccessToFolder(string folderPath)
        {
            try
            {
                var stream = System.IO.File.Create(string.Concat(folderPath, Path.DirectorySeparatorChar, "Essai.txt"));
                stream.Close();
                System.IO.File.Delete(string.Concat(folderPath, Path.DirectorySeparatorChar, "Essai.txt"));
                return true;
            }
            catch { }
            return false;
        }

        public async Task<string[]> GetAllFilesFromFolderAsync(List<string> folders, SearchParametersDto searchParameters, CancellationToken token)
        {
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

            if(options.MinSize == default)
                options.MinSize = 0;
            if (options.MaxSize == default)
                options.MaxSize = long.MaxValue;

            foreach (var folder in folders)
            {
                try
                {
                    if (folder == null || folder.Equals(string.Empty) || !Directory.Exists(folder))
                        await _notificationContext.Clients.All.SendAsync("Notify", new Notification(NotificationType.Exception, $"The folder {folder} does not exist."));
                    else if(!HasWriteAccessToFolder(folder))
                        await _notificationContext.Clients.All.SendAsync("Notify", new Notification(NotificationType.Exception, $"You don't have access the folder {folder}."));
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
                catch (Exception ex)
                {
                    await _notificationContext.Clients.All.SendAsync("Notify", new Notification(NotificationType.Exception, ex.Message));
                }
            }

            return files.Select(file => file.FullName).ToArray();
        }
    }
}
