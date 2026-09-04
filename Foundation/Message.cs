namespace Foundation;

public class Message<T>
{
    public string MessageId { get; set; } = Guid.NewGuid().ToString();
    public string Producer { get; set; } = string.Empty;
    public T Content { get; set; } = default!;
    public string Signature { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}