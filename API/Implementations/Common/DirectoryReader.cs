using System.Security;
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

    public async Task<string[]> GetAllFilesFromFolderAsync(SearchParameters searchParameters,
        CancellationToken cancellationToken)
    {
        var files = new HashSet<string>();

        var enumerationOptions = new EnumerationOptions
        {
            IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System,
            RecurseSubdirectories = searchParameters.IncludeSubfolders, ReturnSpecialDirectories = true
        };

        foreach (var folder in searchParameters.Folders)
            try
            {
                files.UnionWith(GetFilesInFolder(folder, searchParameters, enumerationOptions));
            }
            catch (ArgumentNullException ex)
            {
                Console.WriteLine(ex.Message);
                await _notificationContext.Clients.All.SendAsync("notify",
                    new Notification(NotificationType.Exception, ex.Message), cancellationToken);
            }
            catch (SecurityException ex)
            {
                Console.WriteLine(ex.Message);
                await _notificationContext.Clients.All.SendAsync("notify",
                    new Notification(NotificationType.Exception, ex.Message), cancellationToken);
            }
            catch (PathTooLongException ex)
            {
                Console.WriteLine(ex.Message);
                await _notificationContext.Clients.All.SendAsync("notify",
                    new Notification(NotificationType.Exception, ex.Message), cancellationToken);
            }
            catch (DirectoryNotFoundException ex)
            {
                Console.WriteLine(ex.Message);
                await _notificationContext.Clients.All.SendAsync("notify",
                    new Notification(NotificationType.Exception, ex.Message), cancellationToken);
            }

        return files.ToArray();
    }

    public static IEnumerable<string> GetFilesInFolder(string folder, SearchParameters searchParameters,
        EnumerationOptions enumerationOptions)
    {
        return new DirectoryInfo(folder).EnumerateFiles("*", enumerationOptions)
            .Where(file =>
                (FileFilter.IsFileToBeIncluded(file.Extension, searchParameters.IncludedFileTypes) ||
                 !FileFilter.IsFileToBeExcluded(file.Extension, searchParameters.ExcludedFileTypes)) &&
                FileFilter.IsFileSizeInRange(file.Length, searchParameters.MinSize, searchParameters.MaxSize))
            .Select(file => file.FullName);
    }
}