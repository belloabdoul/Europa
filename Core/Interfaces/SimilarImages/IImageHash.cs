using Core.Entities.Files;
using Core.Entities.Images;

namespace Core.Interfaces.SimilarImages;

public interface IImageHash
{
    public int Height { get; }
    public int Width { get; }
    public ColorSpace ColorSpace { get; }

    public BitArray GenerateHash(string imagePath, FileType fileType);
}