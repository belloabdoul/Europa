namespace API.Common.Entities
{
    public class RedisConnection
    {
        public string? Host { get; set; }
        public string? Port { get; set; }
        public bool IsSSL { get; set; }
        public string? Password { get; set; }
        public bool AllowAdmin { get; set; }
    }
}
