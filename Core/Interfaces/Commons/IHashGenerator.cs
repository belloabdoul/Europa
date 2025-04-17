namespace Core.Interfaces.Commons;

public interface IHashGenerator
{
    ValueTask<byte[]> GenerateHashAsync(string hypotheticalDuplicate, Func<long, long> getFileLengthToHashFunction,
        CancellationToken cancellationToken = default);
}