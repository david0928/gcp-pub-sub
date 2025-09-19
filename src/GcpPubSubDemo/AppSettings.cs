namespace GcpPubSubDemo;

public sealed record PubSubSettings
{
    public string ProjectId { get; init; } = string.Empty;
    public string TopicId { get; init; } = string.Empty;
    public string SubscriptionId { get; init; } = string.Empty;
}

public sealed record EmulatorSettings
{
    public string Host { get; init; } = "localhost";
    public int Port { get; init; } = 8085;
}
