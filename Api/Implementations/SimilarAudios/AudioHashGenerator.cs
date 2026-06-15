using System.Collections;
using System.Numerics;
using System.Numerics.Tensors;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Aelian.FFT;
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
using Swordfish.NET.Collections;
using AVRounding = Sdcb.FFmpeg.Raw.AVRounding;
using AVSampleFormat = Sdcb.FFmpeg.Raw.AVSampleFormat;
using Ffmpeg = Sdcb.FFmpeg.Raw.ffmpeg;

namespace Api.Implementations.SimilarAudios;

public class AudioHashGenerator : IAudioHashGenerator
{
    private static readonly Profile FingerprintConfiguration = new DefaultProfile();

    private static readonly int SamplesNeedPerFingerprint =
        FingerprintConfiguration.DftSize * 5 - FingerprintConfiguration.Overlap;

    private static readonly int FrequenciesNeededPerFingerprint =
        FingerprintConfiguration.FingerprintSize * FingerprintConfiguration.FrequencyBands;

    private static readonly double[] HanningWindow = FillHanningWindow(FingerprintConfiguration.DftSize);

    private static readonly IMinHashService HashService = MinHashService.MaxEntropy;

    private static readonly FingerprintComparer FingerprintComparer = new();

    private static double[] FillHanningWindow(int length)
    {
        var window = GC.AllocateUninitializedArray<double>(FingerprintConfiguration.DftSize);
        for (var i = 0; i < length; i++)
        {
            window[i] = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (length - 1)));
        }

        return window;
    }

    public async ValueTask<IList<Fingerprint>> GenerateAudioHashesAsync(string path,
        byte[] fileId, bool random = false, CancellationToken cancellationToken = default)
    {
        try
        {
            var sampleChannel = Channel.CreateBounded<double>(
                new BoundedChannelOptions(SamplesNeedPerFingerprint)
                    { SingleReader = true, SingleWriter = true });

            var frequencyChannel = Channel.CreateBounded<double>(
                new BoundedChannelOptions(FrequenciesNeededPerFingerprint * Environment.ProcessorCount)
                    { SingleReader = true, SingleWriter = true });

            var fingerprints = new ConcurrentObservableSortedCollection<Fingerprint>(true, FingerprintComparer);

            var fingerprintsGeneration =
                GenerateFingerprintsAsync(frequencyChannel.Reader, fileId, fingerprints, random, cancellationToken);
            var fftAndFrequencyMapping =
                ApplyFftToOverlappingSamplesAndMapFrequenciesAsync(sampleChannel.Reader, frequencyChannel.Writer,
                    cancellationToken);
            var resampling = ResampleAudioAsync(path, sampleChannel.Writer, cancellationToken);
            await Task.WhenAll(resampling, fftAndFrequencyMapping, fingerprintsGeneration);

            return fingerprints;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
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

                if (sampleConverter.Initialized)
                {
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
                else
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
            }
        }

        singletonFrame.Unref();
        outFrame.Unref();
        sampleWriter.Complete();
    }

    private static async Task ApplyFftMultithreadedAsync(List<double> samplesForFft, int nbBands,
        Memory<double> hanningWindow,
        Memory<double> mappedFrequencies, CancellationToken cancellationToken)
    {
        await Parallel.ForAsync(0, nbBands, cancellationToken,
            (index, _) =>
            {
                ApplyFft(index, samplesForFft, hanningWindow, mappedFrequencies);
                return ValueTask.CompletedTask;
            });
    }

    [SkipLocalsInit]
    private static void ApplyFft(int index, List<double> samplesForFft, Memory<double> hanningWindow,
        Memory<double> mappedFrequencies)
    {
        // Allocate _fingerprintConfiguration.FrameSize + 2 for fft. The last 2 are unused in the forware transform
        Span<double> samples = stackalloc double[FingerprintConfiguration.DftSize];

        // Apply hanning window
        TensorPrimitives.Multiply(CollectionsMarshal.AsSpan(samplesForFft).Slice(
            index * FingerprintConfiguration.Overlap,
            FingerprintConfiguration.DftSize), hanningWindow.Span, samples);

        // Apply fft
        FastFourierTransform.RealFFT(samples, true);

        // Maps the resulting frequencies to our 32 bins
        FingerprintConfiguration.MapFrequencies(MemoryMarshal.Cast<double, Complex>(samples) /*fftResult*/,
            mappedFrequencies.Slice(index * FingerprintConfiguration.FrequencyBands,
                FingerprintConfiguration.FrequencyBands).Span);
    }

    private static async Task ApplyFftToOverlappingSamplesAndMapFrequenciesAsync(ChannelReader<double> sampleReader,
        ChannelWriter<double> frequencyWriter, CancellationToken cancellationToken)
    {
        // The samples to which the hanning window and the fft will be applied
        var samplesForFft = new List<double>(SamplesNeedPerFingerprint);

        var mappedFrequencies =
            GC.AllocateUninitializedArray<double>(FingerprintConfiguration.FrequencyBands *
                                                  FingerprintConfiguration.FingerprintSize);

        // Get the hanning windows for the _fingerprintConfiguration.FrameSize samples to be used by the fft
        await foreach (var sample in sampleReader.ReadAllAsync(cancellationToken))
        {
            samplesForFft.Add(sample);

            int nbBands;
            if (samplesForFft.Count >= SamplesNeedPerFingerprint)
            {
                nbBands = 128;
                await ApplyFftMultithreadedAsync(samplesForFft, nbBands, HanningWindow, mappedFrequencies,
                    cancellationToken);

                foreach (var frequency in mappedFrequencies)
                {
                    await frequencyWriter.WriteAsync(frequency, cancellationToken);
                }

                samplesForFft.RemoveRange(0,
                    SamplesNeedPerFingerprint - FingerprintConfiguration.DftSize + FingerprintConfiguration.Overlap);
                continue;
            }

            if (await sampleReader.WaitToReadAsync(cancellationToken))
                continue;

            nbBands = (samplesForFft.Count - FingerprintConfiguration.DftSize) / FingerprintConfiguration.Overlap;

            if (nbBands <= 0)
                continue;

            await ApplyFftMultithreadedAsync(samplesForFft, nbBands + 1, HanningWindow, mappedFrequencies,
                cancellationToken);

            foreach (var frequency in new ArraySegment<double>(mappedFrequencies, 0,
                         nbBands * FingerprintConfiguration.FrequencyBands))
            {
                await frequencyWriter.WriteAsync(frequency, cancellationToken);
            }
        }

        frequencyWriter.Complete();
    }

    private static int GetNextIndex(bool random)
    {
        return Random.Shared.Next(random ? FingerprintConfiguration.Stride / 2 : FingerprintConfiguration.Stride,
            FingerprintConfiguration.Stride + 1) / FingerprintConfiguration.Overlap;
    }

    private static IEnumerable<int> RandomSequence(int start, int count, bool random)
    {
        var current = start;
        while (current <= count - FrequenciesNeededPerFingerprint)
        {
            yield return current;
            current += GetNextIndex(random) * FingerprintConfiguration.FrequencyBands;
        }
    }

    private static void GenerateFingerprintsMultithreaded(List<double> spectrum, int[] startingIndexes,
        ConcurrentObservableSortedCollection<Fingerprint> fingerprints, byte[] fileId, long sampleStart,
        CancellationToken cancellationToken)
    {
        Parallel.ForEach(startingIndexes, new ParallelOptions{CancellationToken = cancellationToken},
            (index, _) =>
            {
                GenerateFingerprint(
                    CollectionsMarshal.AsSpan(spectrum).Slice(index, FingerprintConfiguration.FingerprintSize), index,
                    fingerprints, fileId, sampleStart);
            });
    }

    private static void GenerateFingerprint(Span<double> spectrum, int index,
        ConcurrentObservableSortedCollection<Fingerprint> fingerprints, byte[] fileId, long sampleStart)
    {
        Span<double> imageForPeak = stackalloc double[FrequenciesNeededPerFingerprint];
        Span<ushort> cachedIndexes = stackalloc ushort[FrequenciesNeededPerFingerprint];
        spectrum.CopyTo(imageForPeak);

        // Normalize image to make smaller peak visible
        Normalize(imageForPeak);

        // Use haar wavelets to detect frequency peaks in image
        HaarWaveletTransform.DecomposeImageInPlace(imageForPeak,
            FingerprintConfiguration.FingerprintSize,
            FingerprintConfiguration.FrequencyBands,
            Math.Sqrt(2));

        PopulateIndexes(FrequenciesNeededPerFingerprint, cachedIndexes);
        QuickSelectAlgorithm.Find(200 - 1, imageForPeak, cachedIndexes, 0,
            FrequenciesNeededPerFingerprint - 1);

        // Encode the peaks into a fingerprint, apply the locality-sensitive hashing and add the fingerprint
        // to our list if it only if the image is not empty
        var image = EncodeFingerprint(imageForPeak, cachedIndexes, 200);

        fingerprints.Add(new Fingerprint
        {
            FileHash = fileId, StartAt = (sampleStart + (double)index /
                                             FingerprintConfiguration.FrequencyBands) *
                                         FingerprintConfiguration.Overlap /
                                         FingerprintConfiguration.SampleRate,
            HashBins = HashService.Hash<int>(image, 100)
        });
    }

    private static async Task GenerateFingerprintsAsync(ChannelReader<double> frequencyReader, byte[] fileId,
        ConcurrentObservableSortedCollection<Fingerprint> fingerprints, bool random,
        CancellationToken cancellationToken)
    {
        var spectrum = new List<double>(FrequenciesNeededPerFingerprint * Environment.ProcessorCount);
        var sampleStart = 0L;
        // With static incrementation move forward by 92,8 ms, else by a 46.4, 58, 69.6, 81.2 or 92,8 ms
        await foreach (var frequency in frequencyReader.ReadAllAsync(cancellationToken))
        {
            spectrum.Add(frequency);

            int[] startingIndexes;
            if (spectrum.Count >= FrequenciesNeededPerFingerprint * Environment.ProcessorCount)
            {
                startingIndexes = RandomSequence(0, spectrum.Count, random).ToArray();
                var lastIndex = startingIndexes[^1];
                GenerateFingerprintsMultithreaded(spectrum, startingIndexes, fingerprints, fileId, sampleStart,
                    cancellationToken);
                var fingerprintStart = lastIndex / FingerprintConfiguration.FrequencyBands + GetNextIndex(random);
                sampleStart += fingerprintStart;

                spectrum.RemoveRange(0, fingerprintStart * FingerprintConfiguration.FrequencyBands);
                continue;
            }

            if (await frequencyReader.WaitToReadAsync(cancellationToken))
                continue;

            startingIndexes = RandomSequence(0, spectrum.Count, random).ToArray();
            GenerateFingerprintsMultithreaded(spectrum, startingIndexes, fingerprints, fileId, sampleStart,
                cancellationToken);
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
        Span<double> temp = stackalloc double[frame.Length];
        Span<double> ones = stackalloc double[frame.Length];
        ones.Fill(1);
        TensorPrimitives.Abs(frame, temp);
        var max = TensorPrimitives.Max<double>(temp);

        const int domain = 255;
        var c = 1 / Math.Log(1 + domain);

        TensorPrimitives.Divide(frame, max, frame);
        TensorPrimitives.Min(frame, ones, frame);
        TensorPrimitives.MultiplyAdd(frame, domain, ones, frame);
        TensorPrimitives.Log(frame, frame);
        TensorPrimitives.Multiply(frame, c, frame);
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