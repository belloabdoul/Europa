using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Entities.Redis;

public class ByteVectorJsonConverter : JsonConverter<byte[]>
{
    public override byte[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var hash = new List<byte>();
        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.EndArray)
                hash.Add(Convert.ToByte(reader.GetInt16()));
        }

        return hash.ToArray();
    }

    public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();

        foreach (var binaryValue in value) writer.WriteNumberValue(binaryValue);

        writer.WriteEndArray();
    }
}