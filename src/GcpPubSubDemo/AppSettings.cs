namespace GcpPubSubDemo;

public sealed record PubSubSettings
{
    public string ProjectId { get; init; } = string.Empty;
    public string TopicId { get; init; } = string.Empty;
    public string SubscriptionId { get; init; } = string.Empty;
    /// <summary>
    /// 是否使用本機 Emulator。如果為 true，會使用 PUBSUB_EMULATOR_HOST；否則使用正式 GCP。
    /// </summary>
    public bool UseEmulator { get; init; } = true;
    /// <summary>
    /// 指向服務帳戶 JSON 檔路徑；僅在 UseEmulator = false 時使用。若為空則交由 ADC。
    /// </summary>
    public string? CredentialsPath { get; init; }
}

public sealed record EmulatorSettings
{
    public string Host { get; init; } = "localhost";
    public int Port { get; init; } = 8085;
}
