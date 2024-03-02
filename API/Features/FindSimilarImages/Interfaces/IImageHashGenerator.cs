namespace API.Features.FindSimilarImages.Interfaces
{
    public interface IImageHashGenerator
    {
        string GenerateImageHash(string path, string type);
    }
}
