using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Entities.Redis;

public class ImageHashJsonConverter : JsonConverter<float[]>
{
    public override float[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var hash = ImmutableArray.CreateBuilder<float>();
        while (reader.Read())
        {
            if (reader.TokenType != JsonTokenType.EndArray)
                hash.Add(reader.GetInt16());
        }

        return ImmutableCollectionsMarshal.AsArray(hash.ToImmutableArray());
    }

    public override void Write(Utf8JsonWriter writer, float[] value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
        {
            writer.WriteNumberValue(item);
        }

        writer.WriteEndArray();
    }
}