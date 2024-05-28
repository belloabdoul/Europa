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

    private static readonly HashSet<string> ImageFormats =
    [
        ".mrw", ".arw", ".srf", ".sr2", ".mef", ".orf", ".erf", ".kdc", ".rw2", ".raf", ".dcr", ".dng", ".pef", ".crw",
        ".iiq", ".nrw", ".nef", ".cr2", ".jpg", ".jpeg", ".png", ".bmp", ".tiff", ".tif", ".tga", ".ff", ".webp",
        ".gif", ".ico", ".exr", ".qoi", ".jpe", ".heif", ".heic", ".avifs", ".avif"
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

    public async Task<SortedList<string, (long, DateTime)>> GetAllFilesFromFolderAsync(SearchParameters searchParameters,
        CancellationToken cancellationToken)
    {
        var files = new SortedList<string, (long, DateTime)>();

        var fileTypes = searchParameters.FileSearchType switch
        {
            FileSearchType.Images => ImageFormats,
            FileSearchType.Audios => AudioFormats,
            _ => [".*"]
        };

        if (searchParameters.IncludedFileTypes.Count > 0)
        {
            if (fileTypes.Contains("*"))
                fileTypes = searchParameters.IncludedFileTypes;
            else
            {
                fileTypes.IntersectWith(searchParameters.IncludedFileTypes);
            }
        }

        foreach (var folder in searchParameters.Folders)
        {
            try
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
                    await _notificationContext.Clients.All.SendAsync("Notify",
                        new Notification(NotificationType.Exception, $"The folder {folder} does not exist."),
                        cancellationToken: cancellationToken);
                else if (!HasWriteAccessToFolder(folder))
                    await _notificationContext.Clients.All.SendAsync("Notify",
                        new Notification(NotificationType.Exception, $"You don't have access the folder {folder}."),
                        cancellationToken: cancellationToken);
                else
                {
                    var results = fileTypes
                        .AsParallel()
                        .WithCancellation(cancellationToken)
                        .SelectMany(searchPattern =>
                            new DirectoryInfo(folder).EnumerateFiles(string.Concat("*", searchPattern),
                                searchParameters.IncludeSubfolders
                                    ? SearchOption.AllDirectories
                                    : SearchOption.TopDirectoryOnly))
                        .Where(file => !searchParameters.ExcludedFileTypes.Any(extension =>
                                           file.Extension.Equals(extension,
                                               StringComparison.OrdinalIgnoreCase)) &&
                                       file.Length >= searchParameters.MinSize &&
                                       file.Length <= searchParameters.MaxSize)
                        .Select(file =>
                            new KeyValuePair<string, (long, DateTime)>(file.FullName,
                                (file.Length, file.LastWriteTime))).ToList();
                    files.Capacity += results.Count;
                    results.ForEach(info => files.Add(info.Key, info.Value));
                }
            }
            catch (Exception ex)
            {
                await _notificationContext.Clients.All.SendAsync("Notify",
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