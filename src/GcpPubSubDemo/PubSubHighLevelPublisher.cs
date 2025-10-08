using Google.Api.Gax;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace GcpPubSubDemo;

/// <summary>
/// 使用高階 <see cref="PublisherClient"/>，支援 batching 與背景傳送。
/// 會在 Dispose / Shutdown 時呼叫 ShutdownAsync 以 flush 佇列。
/// </summary>
public sealed class PubSubHighLevelPublisher : IPubSubPublisher, IAsyncDisposable
{
    private readonly PublisherClient _publisherClient;
    private readonly ILogger<PubSubHighLevelPublisher> _logger;

    public PubSubHighLevelPublisher(PublisherClient publisherClient, ILogger<PubSubHighLevelPublisher> logger)
    {
        _publisherClient = publisherClient;
        _logger = logger;
    }

    public async Task<string> PublishAsync(string message, CancellationToken cancellationToken = default)
    {
        try
        {
            var msg = new PubsubMessage
            {
                Data = ByteString.CopyFromUtf8(message),
                Attributes = { { "published_at", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() } }
            };
            var id = await _publisherClient.PublishAsync(msg).ConfigureAwait(false);
            _logger.LogInformation("[HighLevel] Published message {MessageId}", id);
            return id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[HighLevel] Failed to publish message");
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _logger.LogInformation("[HighLevel] Shutting down PublisherClient...");
            await _publisherClient.ShutdownAsync(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[HighLevel] Error during PublisherClient shutdown");
        }
    }
}
