using Google.Cloud.PubSub.V1;
using Microsoft.Extensions.Logging;

namespace GcpPubSubDemo;

public interface IPubSubBootstrap
{
    Task EnsureInfrastructureAsync(CancellationToken ct = default);
}

public sealed class PubSubBootstrap : IPubSubBootstrap
{
    private readonly PublisherServiceApiClient _publisher;
    private readonly SubscriberServiceApiClient _subscriber;
    private readonly PubSubSettings _settings;
    private readonly ILogger<PubSubBootstrap> _logger;

    public PubSubBootstrap(PublisherServiceApiClient publisher, SubscriberServiceApiClient subscriber, PubSubSettings settings, ILogger<PubSubBootstrap> logger)
    {
        _publisher = publisher;
        _subscriber = subscriber;
        _settings = settings;
        _logger = logger;
    }

    public async Task EnsureInfrastructureAsync(CancellationToken ct = default)
    {
        var topicName = TopicName.FromProjectTopic(_settings.ProjectId, _settings.TopicId);
        var subName = SubscriptionName.FromProjectSubscription(_settings.ProjectId, _settings.SubscriptionId);

        try
        {
            await _publisher.GetTopicAsync(topicName, ct);
            _logger.LogInformation("Topic {Topic} exists", topicName);
        }
        catch
        {
            await _publisher.CreateTopicAsync(topicName, ct);
            _logger.LogInformation("Created topic {Topic}", topicName);
        }

        try
        {
            await _subscriber.GetSubscriptionAsync(subName, ct);
            _logger.LogInformation("Subscription {Subscription} exists", subName);
        }
        catch
        {
            await _subscriber.CreateSubscriptionAsync(subName, topicName, pushConfig: null, ackDeadlineSeconds: 60, cancellationToken: ct);
            _logger.LogInformation("Created subscription {Subscription}", subName);
        }
    }
}
