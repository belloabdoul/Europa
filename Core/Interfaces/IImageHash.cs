namespace Core.Interfaces;

public interface IImageHash
{
    int GetRequiredWidth();
    int GetRequiredHeight();
    byte[] GenerateHash(byte[] pixels);
}