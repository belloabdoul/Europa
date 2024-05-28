// ReSharper disable UnusedAutoPropertyAccessor.Global

namespace Core.Entities;

public class Notification
{
    public Notification(NotificationType type, string result)
    {
        Type = type;
        Result = result;
    }

    public NotificationType Type { get; set; }
    public string Result { get; set; }
}