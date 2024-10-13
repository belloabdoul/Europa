using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using U8;
using U8.InteropServices;

namespace Core.Entities.Redis;

public class ObservableHashSetJsonConverter : JsonConverter<ObservableHashSet<U8String>>
{
    public override ObservableHashSet<U8String>? Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        var observableHashSet = new ObservableHashSet<U8String>();
        while (reader.Read())
        {
            observableHashSet.Add(U8Marshal.CreateUnsafe(reader.ValueSpan.ToArray()));
        }

        return observableHashSet;
    }

    public override void Write(Utf8JsonWriter writer, ObservableHashSet<U8String> value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}