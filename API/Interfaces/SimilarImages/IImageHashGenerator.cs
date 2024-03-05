namespace API.Interfaces.SimilarImages
{
    public interface IImageHashGenerator
    {
        string GenerateImageHash(string path, string type);
    }
}
