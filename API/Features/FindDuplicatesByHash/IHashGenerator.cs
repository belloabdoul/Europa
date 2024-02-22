namespace API.Features.FindDuplicatesByHash
{
    public interface IHashGenerator
    {
        string GenerateHash(string path);
    }
}
