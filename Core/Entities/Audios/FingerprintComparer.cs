namespace Core.Entities.Audios;

public class FingerprintComparer: IComparer<Fingerprint>
{
    public int Compare(Fingerprint? x, Fingerprint? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (y is null) return 1;
        if (x is null) return -1;
        return x.StartAt.CompareTo(y.StartAt);
    }
}