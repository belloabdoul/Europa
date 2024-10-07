// ReSharper disable UnusedAutoPropertyAccessor.Global

using MessagePack;

namespace Core.Entities;

[MessagePackObject]
public class Notification
{
    public Notification(NotificationType type, string result)
    {
        Type = type;
        Result = result;
    }

    [Key("type")]
    public NotificationType Type { get; set; }

    [Key("result")]
    public string Result { get; set; }
}