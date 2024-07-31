namespace Core.Interfaces;

public interface IHashGenerator
{
    Task<string?> GenerateHashAsync(FileStream fileHandle, long bytesToHash, CancellationToken cancellationToken);
}