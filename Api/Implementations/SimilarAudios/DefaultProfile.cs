//
// Aurio: Audio Processing, Analysis and Retrieval Library
// Copyright (C) 2010-2017  Mario Guggenberger <mg@protyposis.net>
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as
// published by the Free Software Foundation, either version 3 of the
// License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.
//

using System.Numerics;
using Core.Entities.Audios;

namespace Api.Implementations.SimilarAudios;

/// <summary>
/// Implements the default fingerprint frequency mapping profile as described in the paper.
/// </summary>
public class DefaultProfile : Profile
{
    private readonly ushort[] _frequencyBands;

    public DefaultProfile()
    {
        DftSize = 2048;
        Overlap = 64;
        SampleRate = 5512;
        MinFrequency = 318;
        MaxFrequency = 2000;
        FrequencyBands = 32;
        FingerprintSize = 128;
        Stride = 512;
        
        _frequencyBands = GenerateLogFrequenciesDynamicBase();
    }

    private ushort[] GenerateLogFrequenciesDynamicBase()
    {
        var logBase = Math.Exp(Math.Log((float)MaxFrequency / MinFrequency) / FrequencyBands);
        double minCoef = (float)DftSize / SampleRate * MinFrequency;
        var indexes = new ushort[FrequencyBands + 1];
        for (var j = 0; j < FrequencyBands + 1; j++)
        {
            var start = (int)((Math.Pow(logBase, j) - 1.0) * minCoef);
            indexes[j] = (ushort)(start + (int)minCoef);
        }

        return indexes;
    }

    public override void MapFrequencies(ReadOnlySpan<Complex> inputBins, Span<double> outputBins)
    {
        for (var i = 0; i < FrequencyBands; i++)
        {
            outputBins[i] = 0.0;
            int lowBound = _frequencyBands[i];
            int higherBound = _frequencyBands[i + 1];

            for (var k = lowBound; k < higherBound; k++)
            {
                outputBins[i] += Math.Pow(inputBins[k].Magnitude / inputBins.Length, 2);
            }

            outputBins[i] /= higherBound - lowBound;
        }
    }
}