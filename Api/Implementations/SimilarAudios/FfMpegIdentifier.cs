using Core.Entities.Files;
using Core.Interfaces.Commons;
using Core.Interfaces.SimilarAudios;
using Sdcb.FFmpeg.Common;
using Sdcb.FFmpeg.Formats;
using Sdcb.FFmpeg.Toolboxs.Extensions;
using AVMediaType = Sdcb.FFmpeg.Raw.AVMediaType;

namespace Api.Implementations.SimilarAudios;

public class FfMpegIdentifier : IFileTypeIdentifier, IAudioInfosGetter
{
    public FileType GetFileType(string path)
    {
        using var fileInfo = OpenContext(path);
        if (fileInfo == null)
            return FileType.File;

        fileInfo.LoadStreamInfo();
        
        var audioStream = fileInfo.FindBestStreamOrNull(AVMediaType.Audio);
        var videoStream = fileInfo.FindBestStreamOrNull(AVMediaType.Video);
        
        if (!audioStream.HasValue && !videoStream.HasValue)
            return FileType.File;

        if (audioStream.HasValue && videoStream.HasValue)
            return FileType.AudioVideo;
        
        return audioStream.HasValue ? FileType.Audio : FileType.Video;
    }

    private static FormatContext? OpenContext(string path)
    {
        try
        {
            return FormatContext.OpenInputUrl(path);
        }
        catch (FFmpegException)
        {
            return null;
        }
    }

    public int EstimateNumberOfFingerprints(string path, int sampleRate, int dftSize, int overlap,
        int fingerprintLength, int stride)
    {
        using var fileInfo = FormatContext.OpenInputUrl(path);
        fileInfo.LoadStreamInfo();

        // prepare input stream/codec
        var stream = fileInfo.GetAudioStream();
        
        var totalSamples = stream.GetDurationInSeconds() * sampleRate;
        
        // Calculate how many block of 32 bands will be obtained after the dft
        var nbBlockAfterDft = (totalSamples - dftSize) / overlap + 1;
        
        // Calculate how many fingerprint would be obtained after generating the fingerprints while including the stride
        var nbFingerprint = (nbBlockAfterDft - fingerprintLength) / stride;
        return Convert.ToInt32(nbFingerprint);
    }
}