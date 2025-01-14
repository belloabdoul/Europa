using System.Diagnostics.CodeAnalysis;

namespace Core.Entities.Notifications;

[SuppressMessage("ReSharper", "UnusedMember.Global")]
public class Notification(NotificationType type, string result)
{
    public NotificationType Type { get; set; } = type;

    public string Result { get; set; } = result;
}