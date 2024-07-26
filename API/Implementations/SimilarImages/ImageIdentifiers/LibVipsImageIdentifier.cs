using Core.Entities;
using Core.Interfaces.Common;
using NetVips;

namespace API.Implementations.SimilarImages.ImageIdentifiers;

public class LibVipsImageIdentifier : IFileTypeIdentifier
{
    public FileType GetFileType(string path)
    {
        FileType fileType;
        try
        {
            using var image = Image.NewFromFile(path, access: Enums.Access.Sequential, failOn: Enums.FailOn.Error);
            var loader = (string)image.Get("vips-loader");
            if (loader.Contains("gif", StringComparison.InvariantCultureIgnoreCase) ||
                loader.Contains("webp", StringComparison.InvariantCultureIgnoreCase))
                fileType = image.GetFields().Contains("n-pages", StringComparer.InvariantCultureIgnoreCase)
                    ? FileType.Animation
                    : FileType.Image;
            else
                fileType = FileType.Image;
        }
        catch (VipsException)
        {
            fileType = FileType.CorruptUnknownOrUnsupported;
        }

        return fileType;
    }
}