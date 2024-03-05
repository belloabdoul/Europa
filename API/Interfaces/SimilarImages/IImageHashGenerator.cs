namespace API.Interfaces.SimilarImages
{
    public interface IImageHashGenerator
    {
        string GenerateImageHash(FileStream fileStream, string type);
    }
}
