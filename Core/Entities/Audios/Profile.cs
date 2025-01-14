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

namespace Core.Entities.Audios;

/// <summary>
/// Profile interface for the fingerprint generator.
/// </summary>
public abstract class Profile
{
    public int DftSize { get; protected init; }

    public int Overlap { get; protected init; }

    public int SampleRate { get; protected init; }

    protected int MinFrequency { get; init; }

    protected int MaxFrequency { get; init; }

    public int FrequencyBands { get; protected init; }
        
    public int FingerprintSize { get; protected init; }
        
    public int Stride { get; protected init; }
        
    /// <summary>
    /// Maps the frequency bins from the FFT result to the target frequency bins that the fingerprint
    /// will be generated from.
    /// </summary>
    /// <param name="inputBins">FFT result bins</param>
    /// <param name="outputBins">Target frequency bins</param>

    public abstract void MapFrequencies(ReadOnlySpan<Complex> inputBins, Span<double> outputBins);
}