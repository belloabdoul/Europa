using System.Numerics;
using KFR.Interfaces;

namespace KFR.Entities.Fft;

public sealed class ComplexFft(int rows, int columns) : IFft<Complex>
{
    private readonly KfrFft<Complex> _instance = new(rows, columns);

    public nint SizeToAllocate => _instance.SizeToAllocate;
    
    public nint Rows => _instance.Rows;

    public nint Columns => _instance.Columns;
    
    public nint Size => _instance.Size;
    
    public bool InPlace => true;

    public void DumpPlanInformation()
    {
        _instance.DumpPlanInformation();
    }
    
    public int GetTempBufferSize() => _instance.GetTempBufferSize();

    public void Forward(Span<Complex> inPlaceBuffer, Span<byte> temp)
    {
        _instance.Forward(inPlaceBuffer, temp);
    }

    public void Forward(Span<Complex> input, Span<Complex> output, Span<byte> temp)
    {
        _instance.Forward(input, output, temp);
    }

    public void Inverse(Span<Complex> inPlaceBuffer, Span<byte> temp)
    {
        _instance.Inverse(inPlaceBuffer, temp);
    }

    public void Inverse(Span<Complex> input, Span<Complex> output, Span<byte> temp)
    {
        _instance.Inverse(input, output, temp);
    }

    public void Dispose()
    {
        _instance.Dispose();
    }
}