using KFR.Interfaces;

namespace KFR.Entities;

public sealed class FloatFft(int rows, int columns) : IFft<float>
{
    private readonly KfrFft<float> _instance = new(rows, columns);

    public nint Rows => _instance.Rows;

    public nint Columns => _instance.Columns;

    public nint Size => _instance.Size;
    
    public nint SizeToAllocate => _instance.SizeToAllocate;

    public bool InPlace => true;

    public void DumpPlanInformation()
    {
        _instance.DumpPlanInformation();
    }
    
    public int GetTempBufferSize() => _instance.GetTempBufferSize();

    public void Forward(Span<float> inPlaceBuffer, Span<byte> temp)
    {
        _instance.Forward(inPlaceBuffer, temp);
    }

    public void Forward(Span<float> input, Span<float> output, Span<byte> temp)
    {
        _instance.Forward(input, output, temp);
    }

    public void Inverse(Span<float> inPlaceBuffer, Span<byte> temp)
    {
        _instance.Inverse(inPlaceBuffer, temp);
    }

    public void Inverse(Span<float> input, Span<float> output, Span<byte> temp)
    {
        _instance.Inverse(input, output, temp);
    }

    public void Dispose()
    {
        _instance.Dispose();
    }
}