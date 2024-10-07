using MessagePack;
using MessagePack.Formatters;
using U8;
using U8.InteropServices;

namespace Core.Entities.Redis;

public class U8StringJsonConverter : IMessagePackFormatter<U8String>
{
    public void Serialize(ref MessagePackWriter writer, U8String value, MessagePackSerializerOptions options)
    {
        writer.WriteStringHeader(value.Length);
        writer.WriteRaw(U8Marshal.AsSpan(value));
    }

    public U8String Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        reader.TryReadStringSpan(out var bytes);
        return U8Marshal.CreateUnsafe(bytes.ToArray());
    }
}