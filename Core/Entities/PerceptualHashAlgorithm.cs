using System.Text.Json.Serialization;

namespace Core.Entities;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PerceptualHashAlgorithm
{
    DifferenceHash,
    PerceptualHash,
    BlockMeanHash
}