using Google.Cloud.PubSub.V1;
using Grpc.Core;
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
            // The GetTopicAsync method requires the "roles/pubsub.viewer" role on the topic or project.
            await _publisher.GetTopicAsync(topicName, ct);
            _logger.LogInformation("Topic {Topic} exists", topicName);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            await _publisher.CreateTopicAsync(topicName, ct);
            _logger.LogInformation("Created topic {Topic}", topicName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while ensuring the topic {Topic}: {Message}", topicName, ex.Message);
        }

        try
        {
            // The GetSubscriptionAsync method requires the "roles/pubsub.viewer" role on the subscription or project.
            await _subscriber.GetSubscriptionAsync(subName, ct);
            _logger.LogInformation("Subscription {Subscription} exists", subName);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            await _subscriber.CreateSubscriptionAsync(subName, topicName, pushConfig: null, ackDeadlineSeconds: 60, cancellationToken: ct);
            _logger.LogInformation("Created subscription {Subscription}", subName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An error occurred while ensuring the subscription {Subscription}: {Message}", subName, ex.Message);
        }
    }
}
