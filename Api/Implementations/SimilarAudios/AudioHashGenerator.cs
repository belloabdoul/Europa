using System.Collections;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Api.Implementations.SimilarAudios.MinHash;
using Api.Implementations.SimilarImages.ImageHashGenerators;
using Core.Entities.Audios;
using Core.Interfaces.SimilarAudios;
using DotNext.Buffers;
using Sdcb.FFmpeg.Codecs;
using Sdcb.FFmpeg.Formats;
using Sdcb.FFmpeg.Swresamples;
using Sdcb.FFmpeg.Toolboxs.Extensions;
using Sdcb.FFmpeg.Utils;
using AVRounding = Sdcb.FFmpeg.Raw.AVRounding;
using AVSampleFormat = Sdcb.FFmpeg.Raw.AVSampleFormat;
using Ffmpeg = Sdcb.FFmpeg.Raw.ffmpeg;

namespace Api.Implementations.SimilarAudios;

public class AudioHashGenerator : IAudioHashGenerator
{
    private static readonly Profile FingerprintConfiguration = new DefaultProfile();
    private static readonly MinHashService MinHashService = MinHashService.MaxEntropy;
    public Profile FingerprintingConfiguration => FingerprintConfiguration;

    public async ValueTask<List<Fingerprint>> GenerateAudioHashesAsync(string path,
        byte[] fileId, bool random = false, CancellationToken cancellationToken = default)
    {
        var sampleChannel = Channel.CreateBounded<double>(new BoundedChannelOptions(FingerprintConfiguration.DftSize)
            { SingleReader = true, SingleWriter = true });

        var frequencyChannel = Channel.CreateBounded<double>(
            new BoundedChannelOptions(
                    FingerprintConfiguration.FrequencyBands * FingerprintConfiguration.FingerprintSize)
                { SingleReader = true, SingleWriter = true });

        var fingerprints = new List<Fingerprint>();

        var fingerprintsGeneration =
            GenerateFingerprintsAsync(frequencyChannel.Reader, fileId, fingerprints, random, cancellationToken);
        var fftAndFrequencyMapping =
            ApplyFftToOverlappingSamplesAndMapFrequenciesAsync(sampleChannel.Reader, frequencyChannel.Writer,
                cancellationToken);
        var resampling = ResampleAudioAsync(path, sampleChannel.Writer, cancellationToken);
        await Task.WhenAll(resampling, fftAndFrequencyMapping, fingerprintsGeneration);

        return fingerprints;
    }

    private static async Task ResampleAudioAsync(string path, ChannelWriter<double> sampleWriter,
        CancellationToken cancellationToken)
    {
        using var fileInfo = FormatContext.OpenInputUrl(path);
        fileInfo.LoadStreamInfo();

        // prepare input stream/codec
        var stream = fileInfo.GetAudioStream();
        using var audioDecoder = new CodecContext(Codec.FindDecoderById(stream.Codecpar!.CodecId));
        audioDecoder.FillParameters(stream.Codecpar);
        audioDecoder.Open();

        using var outFrame = Frame.CreateAudio(AVSampleFormat.Dbl, Ffmpeg.AV_CHANNEL_LAYOUT_MONO,
            FingerprintConfiguration.SampleRate, FingerprintConfiguration.DftSize);

        using var sampleConverter = new SampleConverter();
        using var singletonFrame = new Frame();
        foreach (var singletonPacket in fileInfo.ReadPackets(stream.Index))
        {
            foreach (var frame in audioDecoder.DecodePacket(singletonPacket, singletonFrame))
            {
                if (frame.SampleRate <= 0)
                    continue;

                if (!sampleConverter.Initialized)
                {
                    sampleConverter.Options.Set("in_chlayout", frame.ChLayout);
                    sampleConverter.Options.Set("in_sample_rate", frame.SampleRate);
                    sampleConverter.Options.Set("in_sample_fmt", frame.Format);
                    sampleConverter.Options.Set("out_chlayout", Ffmpeg.AV_CHANNEL_LAYOUT_MONO);
                    sampleConverter.Options.Set("out_sample_rate", FingerprintConfiguration.SampleRate);
                    sampleConverter.Options.Set("out_sample_fmt", AVSampleFormat.Dbl);
                    sampleConverter.Options.Set("resampler", "soxr");
                    sampleConverter.Initialize();
                }

                var destSampleCount = (int)Ffmpeg.av_rescale_rnd(
                    sampleConverter.GetDelay(frame.SampleRate) + frame.NbSamples, frame.SampleRate,
                    outFrame.SampleRate, AVRounding.Up);

                var samplesDone =
                    sampleConverter.Convert(outFrame.Data, destSampleCount, frame.Data, frame.NbSamples);

                singletonPacket.Unref();

                Memory<double> samples;
                unsafe
                {
                    samples = UnmanagedMemory.AsMemory((double*)outFrame.Data[0].ToPointer(), samplesDone);
                }

                for (var i = 0; i < samples.Length; i++)
                {
                    await sampleWriter.WriteAsync(samples.Span[i], cancellationToken);
                }
            }
        }

        singletonFrame.Unref();
        outFrame.Unref();

        sampleWriter.Complete();
    }

    private static async Task ApplyFftToOverlappingSamplesAndMapFrequenciesAsync(ChannelReader<double> sampleReader,
        ChannelWriter<double> frequencyWriter, CancellationToken cancellationToken)
    {
        // The samples to which the hanning window and the fft will be applied
        var samples = new List<double>(FingerprintConfiguration.DftSize);

        FillHanningWindow(FingerprintConfiguration.DftSize, out var hanningWindow);

        // Allocate _fingerprintConfiguration.FrameSize + 2 for fft. The last 2 are unused in the forware transform
        var samplesForFft = GC.AllocateUninitializedArray<double>(FingerprintConfiguration.DftSize + 2);
        var frequencyMapped = GC.AllocateUninitializedArray<double>(FingerprintConfiguration.FrequencyBands);

        // Get the hanning windows for the _fingerprintConfiguration.FrameSize samples to be used by the fft
        // Initialize the fft
        var fft = new FftFlat.RealFourierTransform(FingerprintConfiguration.DftSize);

        await foreach (var sample in sampleReader.ReadAllAsync(cancellationToken))
        {
            if (samples.Count >= FingerprintConfiguration.DftSize)
            {
                // Apply the hanning window then the fft.
                CopyAndWindow(samplesForFft, CollectionsMarshal.AsSpan(samples), hanningWindow.Span);
                var fftResult = fft.Forward(samplesForFft);

                FingerprintConfiguration.MapFrequencies(fftResult, frequencyMapped);

                // Maps the resulting frequencies to our 32 bins
                foreach (var t in frequencyMapped)
                {
                    await frequencyWriter.WriteAsync(t, cancellationToken);
                }

                // Remove the first 64 samples to move forward by 64 samples and add the sample which triggered this path
                samples.RemoveRange(0, FingerprintConfiguration.Overlap);
            }

            samples.Add(sample);
        }

        frequencyWriter.Complete();
    }

    private static int GetNextIndex(bool random)
    {
        return Random.Shared.Next(random ? FingerprintConfiguration.Stride / 2 : FingerprintConfiguration.Stride,
            FingerprintConfiguration.Stride + 1) / FingerprintConfiguration.Overlap;
    }

    private async Task GenerateFingerprintsAsync(ChannelReader<double> frequencyWriter, byte[] fileId,
        List<Fingerprint> fingerprints, bool random, CancellationToken cancellationToken)
    {
        var fullImageLength = FingerprintConfiguration.FingerprintSize * FingerprintConfiguration.FrequencyBands;
        var spectrum = new List<double>(fullImageLength);
        var imageForPeak = GC.AllocateUninitializedArray<double>(fullImageLength);
        var cachedIndexes = GC.AllocateUninitializedArray<ushort>(fullImageLength);
        var startAt = 0.0;
        // With static incrementation move forward by 92,8 ms, else by a 46.4, 58, 69.6, 81.2 or 92,8 ms
        await foreach (var frequency in frequencyWriter.ReadAllAsync(cancellationToken))
        {
            if (spectrum.Count >= fullImageLength)
            {
                spectrum.CopyTo(imageForPeak);

                // Normalize image to make smaller peak visible
                Normalize(imageForPeak);

                // Use haar wavelets to detect frequency peaks in image
                HaarWaveletTransform.DecomposeImageInPlace(imageForPeak, FingerprintConfiguration.FingerprintSize,
                    FingerprintConfiguration.FrequencyBands,
                    Math.Sqrt(2));

                PopulateIndexes(fullImageLength, cachedIndexes);
                QuickSelectAlgorithm.Find(200 - 1, imageForPeak, cachedIndexes, 0, fullImageLength - 1);

                // Encode the peaks into a fingerprint, apply the locality-sensitive hashing and add the fingerprint
                // to our list if it only if the image is not empty
                var image = EncodeFingerprint(imageForPeak, cachedIndexes, 200);

                fingerprints.Add(new Fingerprint
                {
                    FileHash = fileId, StartAt = startAt,
                    HashBins = MinHashService.Hash(image, 100)
                });

                var nextFingerprintStart = GetNextIndex(random);
                spectrum.RemoveRange(0, nextFingerprintStart * FingerprintConfiguration.FrequencyBands);
                startAt += nextFingerprintStart * 0.0116;
            }

            spectrum.Add(frequency);
        }
    }

    private static void CopyAndWindow(Span<double> fftArray, Span<double> samples, Span<double> window)
    {
        for (var j = 0; j < window.Length; ++j)
        {
            fftArray[j] = samples[j] * window[j];
        }
    }

    private static void FillHanningWindow(int length, out Memory<double> window)
    {
        window = GC.AllocateUninitializedArray<double>(FingerprintConfiguration.DftSize);
        for (var i = 0; i < length; i++)
        {
            window.Span[i] = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (length - 1)));
        }
    }

    private static void PopulateIndexes(int till, Span<ushort> indexes)
    {
        for (ushort i = 0; i < till; ++i)
        {
            indexes[i] = i;
        }
    }

    [SkipLocalsInit]
    private static void Normalize(Span<double> frame)
    {
        const int domain = 255;
        var c = 1 / Math.Log(1 + domain);
        Span<double> temp = stackalloc double[frame.Length];
        TensorPrimitives.Abs(frame, temp);
        var max = TensorPrimitives.Max<double>(temp);
        for (var i = 0; i < frame.Length; ++i)
        {
            var value = frame[i];
            var scaled = Math.Min(value / max, 1);
            frame[i] = c * Math.Log(1 + scaled * domain);
        }
    }

    /// <summary>
    ///   Encode the integer representation of the fingerprint into a Boolean array.
    /// </summary>
    /// <param name = "concatenated">Concatenated fingerprint (frames concatenated).</param>
    /// <param name = "indexes">Sorted indexes with the first one with the highest value in array.</param>
    /// <param name = "topWavelets">Number of top wavelets to encode.</param>
    /// <returns>Encoded fingerprint.</returns>
    private static BitArray EncodeFingerprint(Span<double> concatenated, Span<ushort> indexes, int topWavelets)
    {
        var schema = new BitArray(concatenated.Length * 2);
        for (var i = 0; i < topWavelets; i++)
        {
            int index = indexes[i];
            var value = concatenated[i];
            switch (value)
            {
                case > 0:
                    schema[index * 2] = true;
                    break;
                case < 0:
                    schema[index * 2 + 1] = true;
                    break;
            }
        }

        return schema;
    }
}