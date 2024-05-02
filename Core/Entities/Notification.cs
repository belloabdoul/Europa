// ReSharper disable UnusedAutoPropertyAccessor.Global
namespace Core.Entities
{
    public class Notification
    {
        public NotificationType Type { get; set; }
        public string Result { get; set; }

        public Notification(NotificationType type, string result)
        {
            Type = type;
            Result = result;
        }
    }
}
