using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace GcpPubSubDemo;

public interface IPubSubPublisher
{
    Task<string> PublishAsync(string message, CancellationToken cancellationToken = default);
}

public sealed class PubSubPublisher : IPubSubPublisher
{
    private readonly PublisherServiceApiClient _publisherClient;
    private readonly TopicName _topicName;
    private readonly ILogger<PubSubPublisher> _logger;

    public PubSubPublisher(PublisherServiceApiClient publisherClient, PubSubSettings settings, ILogger<PubSubPublisher> logger)
    {
        _publisherClient = publisherClient;
        _topicName = TopicName.FromProjectTopic(settings.ProjectId, settings.TopicId);
        _logger = logger;
    }

    public async Task<string> PublishAsync(string message, CancellationToken cancellationToken = default)
    {
        try
        {
            var pubsubMessage = new PubsubMessage
            {
                Data = ByteString.CopyFromUtf8(message),
                Attributes = { { "published_at", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() } }
            };
            var response = await _publisherClient.PublishAsync(_topicName, new[] { pubsubMessage });
            _logger.LogInformation("Published message {MessageId}", response.MessageIds.FirstOrDefault());
            return response.MessageIds.FirstOrDefault() ?? string.Empty;
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "Failed to publish message");
            throw;
        }
    }
}
