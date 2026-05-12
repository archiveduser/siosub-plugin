namespace SioSub;

public enum ConnectionState
{
    Disabled,
    Disconnected,
    Connecting,
    Connected,
    Error,
}

public sealed class SubscriptionStatus
{
    public Guid SubscriptionId { get; init; }

    public string Name { get; set; } = string.Empty;

    public string ServerUrl { get; set; } = string.Empty;

    public ConnectionState State { get; set; } = ConnectionState.Disconnected;

    public string Detail { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    public IReadOnlyList<string> Events { get; set; } = [];
}
