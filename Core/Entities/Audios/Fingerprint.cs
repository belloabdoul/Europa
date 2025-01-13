namespace Core.Entities.Audios;

public class Fingerprint
{
    public byte[] FileHash { get; init; } = [];
    public double StartAt { get; init; }
    public int[] HashBins { get; init; } = [];
    public const int BucketCount = 25;
}