using System.Text.Json;
using System.Text.Json.Serialization;
using Redis.OM;

namespace Core.Entities.Redis;

public class ByteVectorJsonConverter : JsonConverter<Vector<byte[]>>
{
    private readonly ByteToFloatVectorizerAttribute _vectorizerAttribute;

    /// <summary>
    /// Initializes a new instance of the <see cref="ByteVectorJsonConverter"/> class.
    /// </summary>
    /// <param name="attribute">the attribute that will be used for vectorization.</param>
    internal ByteVectorJsonConverter(ByteToFloatVectorizerAttribute attribute)
    {
        _vectorizerAttribute = attribute;
    }

    public override Vector<byte[]>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        reader.Read();

        var bytes = new byte[_vectorizerAttribute.Dim];
        for (var i = 0; i < _vectorizerAttribute.Dim; i++)
        {
            bytes[i] = Convert.ToByte(reader.GetInt32());
            reader.Read();
        }

        var vector = Vector.Of(bytes);
        vector.Embed(_vectorizerAttribute);
        return vector;
    }

    public override void Write(Utf8JsonWriter writer, Vector<byte[]> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();

        foreach (var binaryValue in value.Value)
        {
            writer.WriteNumberValue(binaryValue);
        }

        writer.WriteEndArray();
    }
}