using Core.Entities;
using Core.Interfaces.Common;
using Microsoft.AspNetCore.SignalR;
using File = System.IO.File;

namespace API.Implementations.Common;

public class DirectoryReader : IDirectoryReader
{
    private static readonly HashSet<string> AudioFormats =
    [
        ".mp3", ".flac", ".wav", ".ogg", ".m4a", ".aac", ".aiff", ".pcm", ".aif", ".aiff", ".aifc", ".m3a", ".mp2",
        ".mp4a", ".mp2a", ".mpga", ".wave", ".weba", ".wma", ".oga"
    ];

    private readonly IHubContext<NotificationHub> _notificationContext;
    
    public DirectoryReader(IHubContext<NotificationHub> notificationContext)
    {
        _notificationContext = notificationContext;
    }

    public bool FileExists(string filePath)
    {
        return File.Exists(filePath);
    }

    public async Task<HashSet<string>> GetAllFilesFromFolderAsync(SearchParameters searchParameters,
        CancellationToken cancellationToken)
    {
        var files = new HashSet<string>();

        foreach (var folder in searchParameters.Folders)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                    await _notificationContext.Clients.All.SendAsync("notify",
                        new Notification(NotificationType.Exception, $"The folder {folder} does not exist."),
                        cancellationToken: cancellationToken);
                else if (!HasWriteAccessToFolder(folder))
                    await _notificationContext.Clients.All.SendAsync("notify",
                        new Notification(NotificationType.Exception, $"You don't have access the folder {folder}."),
                        cancellationToken: cancellationToken);
                else
                {
                    files.UnionWith(new DirectoryInfo(folder).EnumerateFiles("*",
                                searchParameters.IncludeSubfolders
                                    ? SearchOption.AllDirectories
                                    : SearchOption.TopDirectoryOnly)
                        .Where(file => (FileFilter.IsFileToBeIncluded(file.Extension, searchParameters.IncludedFileTypes) || !FileFilter.IsFileToBeExcluded(file.Extension, searchParameters.ExcludedFileTypes)) &&
                                       FileFilter.IsFileSizeInRange(file.Length, searchParameters.MinSize, searchParameters.MaxSize))
                        .Select(file => file.FullName));
                }
            }
            catch (Exception ex)
            {
                await _notificationContext.Clients.All.SendAsync("notify",
                    new Notification(NotificationType.Exception, ex.Message), cancellationToken: cancellationToken);
            }
        }

        return files;
    }

    private static bool HasWriteAccessToFolder(string folderPath)
    {
        try
        {
            var stream = File.Create(string.Concat(folderPath, Path.DirectorySeparatorChar, "Essai.txt"));
            stream.Close();
            File.Delete(string.Concat(folderPath, Path.DirectorySeparatorChar, "Essai.txt"));
            return true;
        }
        catch
        {
            return false;
        }
    }
}