namespace Core.Interfaces;

public interface IImageHash
{
    public byte[] GenerateHash(string path);
}