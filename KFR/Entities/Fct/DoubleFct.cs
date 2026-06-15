using KFR.Interfaces;

namespace KFR.Entities.Fct;

public sealed class DoubleFct(int size) : IFct<double>
{
    private readonly KfrFct<double> _instance = new(size);

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