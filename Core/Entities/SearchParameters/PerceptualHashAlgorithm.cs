using System.Text.Json.Serialization;

namespace Core.Entities.SearchParameters;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PerceptualHashAlgorithm
{
    QDctHash
}