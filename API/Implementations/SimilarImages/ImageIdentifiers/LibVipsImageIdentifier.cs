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
            using var image = Image.NewFromFile(path, access: Enums.Access.Random, failOn: Enums.FailOn.Error);
            var loader = (string)image.Get("vips-loader");
            if (loader.Contains("gif", StringComparison.InvariantCultureIgnoreCase) ||
                loader.Contains("webp", StringComparison.InvariantCultureIgnoreCase))
                fileType = (int) image.Get("n-pages") > 1 ? FileType.Animation : FileType.Image;
            else
                fileType = FileType.Image;
        }
        catch (VipsException e)
        {
            fileType = FileType.Corrupt;
        }

        return fileType;
    }
}