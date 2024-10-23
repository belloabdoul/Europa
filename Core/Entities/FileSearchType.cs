using System.Text.Json.Serialization;

namespace Core.Entities;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FileSearchType
{
    All,
    Audios,
    Images
}