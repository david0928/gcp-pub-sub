using Google.Cloud.PubSub.V1;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GcpPubSubDemo;

public interface IPubSubSubscriber { }

public sealed class PubSubSubscriber : BackgroundService, IPubSubSubscriber
{
    private readonly SubscriberServiceApiClient _subscriberClient;
    private readonly SubscriptionName _subscriptionName;
    private readonly ILogger<PubSubSubscriber> _logger;

    public PubSubSubscriber(SubscriberServiceApiClient subscriberClient, PubSubSettings settings, ILogger<PubSubSubscriber> logger)
    {
        _subscriberClient = subscriberClient;
        _subscriptionName = SubscriptionName.FromProjectSubscription(settings.ProjectId, settings.SubscriptionId);
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Subscriber starting for {Subscription}", _subscriptionName);
        await Task.Yield();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 以 Pull 模式批次取得訊息（簡化 emulator 範例）
                var pullRequest = new PullRequest
                {
                    SubscriptionAsSubscriptionName = _subscriptionName,
                    MaxMessages = 1
                };
                var response = await _subscriberClient.PullAsync(pullRequest, cancellationToken: stoppingToken);
                if (response.ReceivedMessages.Count == 0)
                {
                    continue;
                }
                var ackIds = new List<string>();
                foreach (var received in response.ReceivedMessages)
                {
                    var data = received.Message.Data.ToStringUtf8();
                    _logger.LogInformation("Received message {MessageId}: {Data}", received.Message.MessageId, data);
                    ackIds.Add(received.AckId);
                }
                if (ackIds.Count > 0)
                {
                    await _subscriberClient.AcknowledgeAsync(_subscriptionName, ackIds, cancellationToken: stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // graceful shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in pull loop, retrying in 2s");
                try { await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken); } catch { }
            }
        }
        _logger.LogInformation("Subscriber stopping");
    }
}
