namespace API.Features.FindDuplicatesByHash.Interfaces
{
    public interface IHashGenerator
    {
        string GenerateHash(string path);
    }
}
