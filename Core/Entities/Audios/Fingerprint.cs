namespace Core.Entities.Audios;

public class Fingerprint
{
    public Guid Id { get; set; }
    public byte[] FileHash { get; set; } = [];
    public double StartAt { get; set; }
    public int[] HashBins { get; set; } = [];
    public int Score;
    public const int BucketCount = 25;
}