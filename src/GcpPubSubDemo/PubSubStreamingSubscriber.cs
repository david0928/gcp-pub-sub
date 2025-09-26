using Google.Api.Gax.Grpc;
using Google.Cloud.PubSub.V1;
using Grpc.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GcpPubSubDemo;

public sealed class PubSubStreamingSubscriber : BackgroundService
{
    private readonly SubscriberServiceApiClient _subscriberClient;
    private readonly SubscriptionName _subscriptionName;
    private readonly ILogger<PubSubStreamingSubscriber> _logger;

    public PubSubStreamingSubscriber(SubscriberServiceApiClient subscriberClient, PubSubSettings settings, ILogger<PubSubStreamingSubscriber> logger)
    {
        _subscriberClient = subscriberClient;
        _subscriptionName = SubscriptionName.FromProjectSubscription(settings.ProjectId, settings.SubscriptionId);
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[Streaming] Subscriber starting for {Subscription}", _subscriptionName);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var streaming = _subscriberClient.StreamingPull(CallSettings.FromCancellationToken(stoppingToken));
                await streaming.WriteAsync(new StreamingPullRequest
                {
                    SubscriptionAsSubscriptionName = _subscriptionName,
                    StreamAckDeadlineSeconds = 60,
                    MaxOutstandingMessages = 1,
                });

                var responseStream = streaming.GetResponseStream();
                var pendingAckIds = new List<string>();
                while (!stoppingToken.IsCancellationRequested && await responseStream.MoveNextAsync(stoppingToken).ConfigureAwait(false))
                {
                    var resp = responseStream.Current;
                    if (resp == null) continue;
                    if (resp.ReceivedMessages.Count == 0) continue;
                    foreach (var received in resp.ReceivedMessages)
                    {
                        var data = received.Message.Data.ToStringUtf8();
                        _logger.LogInformation("[Streaming] Received {MessageId}: {Data}", received.Message.MessageId, data);
                        pendingAckIds.Add(received.AckId);
                    }
                    if (pendingAckIds.Count > 0)
                    {
                        await streaming.WriteAsync(new StreamingPullRequest { AckIds = { pendingAckIds } });
                        pendingAckIds.Clear();
                    }
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled && stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("[Streaming] Cancelled due to shutdown");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Streaming] Loop error, retry in 3s");
                try { await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken); } catch { }
            }
        }
        _logger.LogInformation("[Streaming] Subscriber stopping");
    }
}
