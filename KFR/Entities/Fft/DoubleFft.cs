using KFR.Interfaces;

namespace KFR.Entities.Fft;

public sealed class DoubleFft(int rows, int columns) : IFft<double>
{
    private readonly KfrFft<double> _instance = new(rows, columns);

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

    public void Forward(Span<double> inPlaceBuffer, Span<byte> temp)
    {
        _instance.Forward(inPlaceBuffer, temp);
    }

    public void Forward(Span<double> input, Span<double> output, Span<byte> temp)
    {
        _instance.Forward(input, output, temp);
    }

    public void Inverse(Span<double> inPlaceBuffer, Span<byte> temp)
    {
        _instance.Inverse(inPlaceBuffer, temp);
    }

    public void Inverse(Span<double> input, Span<double> output, Span<byte> temp)
    {
        _instance.Inverse(input, output, temp);
    }

    public void Dispose()
    {
        _instance.Dispose();
    }
}