using System.Buffers;
using System.Security;
using Core.Entities.Notifications;
using Core.Entities.SearchParameters;
using Core.Interfaces.Commons;
using Microsoft.AspNetCore.SignalR;

namespace Api.Implementations.Commons;

public class DirectoryReader(IHubContext<NotificationHub> notificationContext) : IDirectoryReader
{
    public async Task<List<string>> GetAllFilesFromFolderAsync(SearchParameters searchParameters,
        CancellationToken cancellationToken)
    {
        var files = new List<string>();

        var fileTypesToInclude = SearchValues.Create(searchParameters.IncludedFileTypes,
            StringComparison.OrdinalIgnoreCase);
        var fileTypesToExclude = SearchValues.Create(searchParameters.ExcludedFileTypes,
            StringComparison.OrdinalIgnoreCase);

        foreach (var folder in searchParameters.Folders)
            try
            {
                files.AddRange(GetFilesInFolder(folder, searchParameters.MinSize, searchParameters.MaxSize,
                    fileTypesToInclude, fileTypesToExclude,
                    searchParameters.IncludeSubFolders));
            }
            catch (ArgumentNullException ex)
            {
                Console.WriteLine(ex.Message);
                await notificationContext.Clients.All.SendAsync("notify",
                    new Notification(NotificationType.Exception, ex.Message), cancellationToken);
            }
            catch (SecurityException ex)
            {
                Console.WriteLine(ex.Message);
                await notificationContext.Clients.All.SendAsync("notify",
                    new Notification(NotificationType.Exception, ex.Message), cancellationToken);
            }
            catch (PathTooLongException ex)
            {
                Console.WriteLine(ex.Message);
                await notificationContext.Clients.All.SendAsync("notify",
                    new Notification(NotificationType.Exception, ex.Message), cancellationToken);
            }
            catch (DirectoryNotFoundException ex)
            {
                Console.WriteLine(ex.Message);
                await notificationContext.Clients.All.SendAsync("notify",
                    new Notification(NotificationType.Exception, ex.Message), cancellationToken);
            }

        files.TrimExcess();
        return files;
    }

    public IEnumerable<string> GetFilesInFolder(string folder, long? minSize, long? maxSize,
        SearchValues<string> includedFileTypes, SearchValues<string> excludedFileTypes, bool includeSubFolders = false)
    {
        var enumerationOptions = new EnumerationOptions
        {
            IgnoreInaccessible = true, AttributesToSkip = FileAttributes.System,
            RecurseSubdirectories = includeSubFolders, ReturnSpecialDirectories = true
        };

        return new DirectoryInfo(folder).EnumerateFiles("*", enumerationOptions).Where(file =>
                !FileFilter.IsFileToBeExcluded(file.Extension, excludedFileTypes) &&
                FileFilter.IsFileToBeIncluded(file.Extension, includedFileTypes) &&
                FileFilter.IsFileSizeInRange(file.Length, minSize, maxSize))
            .Select(file => file.FullName);
    }
}