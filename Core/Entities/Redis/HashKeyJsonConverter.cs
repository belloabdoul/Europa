using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Entities.Redis;

public class HashKeyJsonConverter: JsonConverter<HashKey>
{
    public override HashKey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return new HashKey(Convert.FromHexString(reader.GetString()!));
    }

    public override void Write(Utf8JsonWriter writer, HashKey value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}