using System.Numerics;

namespace KFR.Interfaces;

public interface IFft<T> : IDisposable where T: struct, ISignedNumber<T>
{
    /// <summary>
    /// The FFT size that this instance expects.
    /// </summary>
    nint Size { get; }
    
    public nint SizeToAllocate { get; }
    
    /// <summary>
    /// The FFT rows that this instance expects.
    /// </summary>
    nint Rows { get; }
    
    /// <summary>
    /// The FFT columns that this instance expects.
    /// </summary>
    nint Columns { get; }

    /// <summary>
    /// Tells if this FFT instance supports in-place transformation. If it does not,
    /// the in-place methods can still be used, but an intermediate buffer with data copies
    /// will be used to emulate the behavior (with negative performance implications).
    /// </summary>
    bool InPlace { get; }

    public void DumpPlanInformation();
    
    /// <summary>
    /// Get buffer size needed for temp buffer in bytes.
    /// </summary>
    public int GetTempBufferSize();
    
    /// <summary>
    /// Transforms samples from the time to the frequency domain.
    /// </summary>
    void Forward(Span<T> inPlaceBuffer, Span<byte> temp);

    /// <summary>
    /// Transforms samples from the time to the frequency domain.
    /// </summary>
    void Forward(Span<T> input, Span<T> output, Span<byte> temp);

    /// <summary>
    /// Transforms samples from the frequency to the time domain.
    /// </summary>
    void Inverse(Span<T> inPlaceBuffer, Span<byte> temp);

    /// <summary>
    /// Transforms samples from the frequency to the time domain.
    /// </summary>
    void Inverse(Span<T> input, Span<T> output, Span<byte> temp);
}