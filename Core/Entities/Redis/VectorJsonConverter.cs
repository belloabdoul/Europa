using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Entities.Redis;

public class VectorJsonConverter : JsonConverter<byte[]>
{
    public int Dim { get; set; }

    public VectorJsonConverter(int dim)
    {
        Dim = dim;
    }

    public override byte[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var hash = new byte[Dim];

        for (var i = 0; i < Dim; i++)
        {
            reader.Read();
            hash[i] = Convert.ToByte(reader.GetInt16());
        }

        return hash;
    }

    public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();

        foreach (var binaryValue in value) writer.WriteNumberValue(binaryValue);

        writer.WriteEndArray();
    }
}