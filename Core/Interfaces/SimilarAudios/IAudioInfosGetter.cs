namespace Core.Interfaces.SimilarAudios;

public interface IAudioInfosGetter
{
    int EstimateNumberOfFingerprints(string path, int sampleRate, int dftSize, int overlap,
        int fingerprintLength, int stride);
}