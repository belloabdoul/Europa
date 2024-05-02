using Core.Entities;
using Core.Interfaces.Common;
using Microsoft.AspNetCore.SignalR;
// ReSharper disable StringLiteralTypo

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

        private static bool HasWriteAccessToFolder(string folderPath)
        {
            try
            {
                var stream = System.IO.File.Create(string.Concat(folderPath, Path.DirectorySeparatorChar, "Essai.txt"));
                stream.Close();
                System.IO.File.Delete(string.Concat(folderPath, Path.DirectorySeparatorChar, "Essai.txt"));
                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<List<string>> GetAllFilesFromFolderAsync(SearchParameters searchParameters, CancellationToken token)
        {
            var files = new List<string>();

            var fileTypes = searchParameters.FileSearchType switch
            {
                FileSearchType.Images => _imageFormats,
                FileSearchType.Audios => _audioFormats,
                _ => ["*"]
            };
            
            if (searchParameters.IncludedFileTypes.Count > 0)
            {
                if (fileTypes.Contains("*"))
                    fileTypes = searchParameters.IncludedFileTypes;
                else
                {
                    foreach (var fileType in fileTypes.Where(fileType => !searchParameters.IncludedFileTypes.Contains(fileType)).ToList())
                    {
                        fileTypes.Remove(fileType);
                    }
                }
            }

            foreach (var folder in searchParameters.Folders)
            {
                try
                {
                    if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                        await _notificationContext.Clients.All.SendAsync("Notify", new Notification(NotificationType.Exception, $"The folder {folder} does not exist."), cancellationToken: token);
                    else if(!HasWriteAccessToFolder(folder))
                        await _notificationContext.Clients.All.SendAsync("Notify", new Notification(NotificationType.Exception, $"You don't have access the folder {folder}."), cancellationToken: token);
                    else
                    {
                        files.AddRange(
                            fileTypes
                            .AsParallel()
                            .WithCancellation(token)
                            .SelectMany(searchPattern => new DirectoryInfo(folder).EnumerateFiles(string.Concat("*.", searchPattern), searchParameters.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
                            .Where(file => file.Length >= searchParameters.MinSize && file.Length <= searchParameters.MaxSize)
                            .Select(file => file.FullName)
                            .ToList()
                        );

                        files = files.AsParallel().Where(file => fileTypes.Any(file.EndsWith) || !searchParameters.ExcludedFileTypes.Any(file.EndsWith)).ToList();
                    }
                }
                catch (Exception ex)
                {
                    await _notificationContext.Clients.All.SendAsync("Notify", new Notification(NotificationType.Exception, ex.Message), cancellationToken: token);
                }
            }

            return files.Distinct().ToList();
        }
    }
}
