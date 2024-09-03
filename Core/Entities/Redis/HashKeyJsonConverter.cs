using System.Text.Json;
using System.Text.Json.Serialization;
using Blake3;

namespace Core.Entities.Redis;

public class HashKeyJsonConverter: JsonConverter<Hash>
{
    public override Hash Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return Hash.FromBytes(Convert.FromHexString(reader.GetString()!));
    }

    public override void Write(Utf8JsonWriter writer, Hash value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}