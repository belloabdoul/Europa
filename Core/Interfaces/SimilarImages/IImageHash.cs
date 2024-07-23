namespace Core.Interfaces.SimilarImages;

public interface IImageHash
{
    public byte[] GenerateHash(string path);
}