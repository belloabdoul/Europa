namespace API.Interfaces.DuplicatesByHash
{
    public interface IHashGenerator
    {
        string GenerateHash(FileStream path);
    }
}
