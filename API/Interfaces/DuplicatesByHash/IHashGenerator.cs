namespace API.Interfaces.DuplicatesByHash
{
    public interface IHashGenerator
    {
        string GenerateHash(string path);
    }
}
