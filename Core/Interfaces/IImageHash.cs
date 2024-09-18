namespace Core.Interfaces;

public interface IImageHash
{
    Half[] GenerateHash(ReadOnlySpan<byte> pixels);
}