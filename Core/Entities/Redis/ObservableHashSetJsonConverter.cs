using MessagePack;
using MessagePack.Formatters;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using U8;
using U8.InteropServices;

namespace Core.Entities.Redis;

public class ObservableHashSetJsonConverter: IMessagePackFormatter<ObservableHashSet<U8String>>
{
    public void Serialize(ref MessagePackWriter writer, ObservableHashSet<U8String> value, MessagePackSerializerOptions options)
    {
        throw new NotImplementedException();
    }

    public ObservableHashSet<U8String> Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        var observableHashSet = new ObservableHashSet<U8String>();
        reader.TryReadArrayHeader(out var count);
        for (var i = 0; i < count; i++)
        {
            reader.TryReadStringSpan(out var utf8String);
            observableHashSet.Add(U8Marshal.CreateUnsafe(utf8String.ToArray()));
        }
        return observableHashSet;
    }
}