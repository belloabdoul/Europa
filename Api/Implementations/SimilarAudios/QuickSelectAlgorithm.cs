namespace Api.Implementations.SimilarAudios;

public static class QuickSelectAlgorithm
{
    public static int Find(int kth, Span<double> list, Span<ushort> indexes, int lo, int hi)
    {
        while (lo != hi)
        {
            var mid = lo + (hi - lo) / 2;
            SwapIfGreater(list, indexes, lo, mid);
            SwapIfGreater(list, indexes, lo, hi);
            SwapIfGreater(list, indexes, mid, hi);
            var pi = mid;
            pi = Partition(list, indexes, pi, lo, hi);
            if (pi == kth)
            {
                return pi;
            }

            if (pi > kth)
            {
                hi = pi - 1;
            }
            else
            {
                lo = pi + 1;
            }
        }

        return lo;
    }

    private static int Partition(Span<double> list, Span<ushort> indexes, int pivotIndex, int lo, int hi)
    {
        var pivotValue = Math.Abs(list[pivotIndex]);
        Swap(list, indexes, pivotIndex, hi);
        var storeIndex = lo;
        for (var i = lo; i < hi; ++i)
        {
            if (Math.Abs(list[i]) <= pivotValue) 
                continue;
            
            Swap(list, indexes, storeIndex, i);
            storeIndex++;
        }

        Swap(list, indexes, hi, storeIndex);
        return storeIndex;
    }

    private static void Swap(Span<double> list, Span<ushort> indexes, int i, int j)
    {
        (list[i], list[j]) = (list[j], list[i]);
        (indexes[i], indexes[j]) = (indexes[j], indexes[i]);
    }

    private static void SwapIfGreater(Span<double> list, Span<ushort> indexes, int a, int b)
    {
        if (a == b) 
            return;
        
        if (Math.Abs(list[a]) > Math.Abs(list[b]))
        {
            Swap(list, indexes, a, b);
        }
    }
}