using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.HighPerformance.Buffers;

namespace Core.Entities.Redis;

public class HashJsonConverter: JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return StringPool.Shared.GetOrAdd(reader.ValueSpan, Encoding.UTF8);
    }

    [SkipLocalsInit]
    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}