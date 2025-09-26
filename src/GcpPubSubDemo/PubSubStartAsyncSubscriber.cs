using Google.Api.Gax;
using Google.Cloud.PubSub.V1;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GcpPubSubDemo;

/// <summary>
/// 使用 Google.Cloud.PubSub.V1 的高階 SubscriberClient.StartAsync 模式。
/// 與手寫 Pull / StreamingPull 相比，StartAsync 會管理底層 StreamingPull 連線與重試邏輯。
/// </summary>
public sealed class PubSubStartAsyncSubscriber : BackgroundService
{
    private readonly SubscriptionName _subscriptionName;
    private readonly SubscriberClient _subscriberClient;
    private readonly ILogger<PubSubStartAsyncSubscriber> _logger;

    public PubSubStartAsyncSubscriber(
        PubSubSettings settings,
        SubscriberClient subscriberClient,
        ILogger<PubSubStartAsyncSubscriber> logger)
    {
        _subscriptionName = SubscriptionName.FromProjectSubscription(settings.ProjectId, settings.SubscriptionId);
        _subscriberClient = subscriberClient;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[StartAsync] Subscriber starting for {Subscription}", _subscriptionName);

        Task? runTask = null;

        try
        {
            runTask = _subscriberClient.StartAsync(async (PubsubMessage msg, CancellationToken ct) =>
            {
                try
                {
                    var data = msg.Data.ToStringUtf8();
                    _logger.LogInformation("[StartAsync] Received {MessageId}: {Data}", msg.MessageId, data);
                    await Task.Yield(); // 模擬處理邏輯 (可在此加入 JSON 解析、商業邏輯等)
                    return SubscriberClient.Reply.Ack;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[StartAsync] Handler error, Nack {MessageId}", msg.MessageId);
                    return SubscriberClient.Reply.Nack;
                }
            });

            // 等待停止
            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }

            _logger.LogInformation("[StartAsync] Stopping subscriber...");
            await _subscriberClient.StopAsync(CancellationToken.None);
            if (runTask is not null) await runTask;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("[StartAsync] Cancelled by shutdown");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[StartAsync] Fatal error");
        }
        finally
        {
            _logger.LogInformation("[StartAsync] Subscriber stopped ({Subscription})", _subscriptionName);
        }
    }
}
