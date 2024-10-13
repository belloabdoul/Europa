using System.Text.Json;
using System.Text.Json.Serialization;
using U8;
using U8.InteropServices;

namespace Core.Entities.Redis;

public class U8StringJsonConverter : JsonConverter<U8String>
{
    public override U8String Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return U8Marshal.CreateUnsafe(reader.ValueSpan.ToArray());
    }

    public override void Write(Utf8JsonWriter writer, U8String value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(U8Marshal.AsSpan(value));
    }
}