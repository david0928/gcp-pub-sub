using GcpPubSubDemo;
using Google.Api.Gax;
using Google.Cloud.PubSub.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// 從組件輸出目錄載入設定 (appsettings.json 已設定 CopyToOutputDirectory)
var configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
builder.Configuration.AddJsonFile(configPath, optional: false, reloadOnChange: false);
var pubSubSettings = builder.Configuration.GetSection("PubSub").Get<PubSubSettings>()!;
var emulatorSettings = builder.Configuration.GetSection("Emulator").Get<EmulatorSettings>()!;

builder.Services.AddSingleton(pubSubSettings);

builder.Services.AddLogging(lb => lb.AddConsole());

// 設定 Emulator endpoint
string emulatorEndpoint = $"{emulatorSettings.Host}:{emulatorSettings.Port}";
Environment.SetEnvironmentVariable("PUBSUB_EMULATOR_HOST", emulatorEndpoint);

// 使用官方 Builder 自動偵測 PUBSUB_EMULATOR_HOST 來建立 Client
builder.Services.AddSingleton(provider => new PublisherServiceApiClientBuilder
{
    EmulatorDetection = EmulatorDetection.EmulatorOnly
}.Build());

builder.Services.AddSingleton(provider => new SubscriberServiceApiClientBuilder
{
    EmulatorDetection = EmulatorDetection.EmulatorOnly
}.Build());

// 新增：註冊高階 SubscriberClient（給 StartAsync 版本使用）
builder.Services.AddSingleton(provider => new SubscriberClientBuilder
{
    SubscriptionName = SubscriptionName.FromProjectSubscription(pubSubSettings.ProjectId, pubSubSettings.SubscriptionId),
    EmulatorDetection = EmulatorDetection.EmulatorOnly,
    Settings = new SubscriberClient.Settings
    {
        FlowControlSettings = new FlowControlSettings(
            maxOutstandingElementCount: 1, // 限制同時處理訊息數量為 1（模擬單線程處理）
            maxOutstandingByteCount: 10 * 1024 * 1024) // 10MB
    }
}.Build());

builder.Services.AddSingleton<IPubSubPublisher, PubSubPublisher>();

// 註冊三種 Subscriber 背景服務
builder.Services.AddHostedService<PubSubSubscriber>();
builder.Services.AddHostedService<PubSubStreamingSubscriber>();
builder.Services.AddHostedService<PubSubStartAsyncSubscriber>();

builder.Services.AddSingleton<IPubSubBootstrap, PubSubBootstrap>();

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

logger.LogInformation("Starting host with Pub/Sub emulator at {Endpoint}", emulatorEndpoint);

// 啟動前確保 Topic / Subscription 存在
var bootstrap = host.Services.GetRequiredService<IPubSubBootstrap>();
await bootstrap.EnsureInfrastructureAsync();

await host.StartAsync();

// 互動式 Publisher 主控台
var publisher = host.Services.GetRequiredService<IPubSubPublisher>();
var pubsubSettings = host.Services.GetRequiredService<PubSubSettings>();
var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();
var cts = new CancellationTokenSource();
lifetime.ApplicationStopping.Register(() => cts.Cancel());

logger.LogInformation("Interactive publisher ready.");
logger.LogInformation("Topic: {Topic} | Subscription: {Sub} | Project: {Project}", pubsubSettings.TopicId, pubsubSettings.SubscriptionId, pubsubSettings.ProjectId);
logger.LogInformation("Commands: /exit 或 /quit 離開, /ts 發送目前 UTC 時間戳, /help 顯示指令");
logger.LogInformation("請輸入訊息後按 Enter 送出 (Ctrl+C 也可離開)...");

var inputTask = Task.Run(async () =>
{
    while (!cts.IsCancellationRequested)
    {
        Console.Write("> ");
        string? line;
        try
        {
            line = Console.ReadLine();
        }
        catch
        {
            // Console 在關閉中
            break;
        }

        if (line is null)
        {
            await Task.Delay(100, cts.Token);
            continue;
        }

        line = line.Trim();
        if (line.Length == 0) continue; // 忽略空白輸入

        switch (line.ToLowerInvariant())
        {
            case "/exit":
            case "/quit":
                logger.LogInformation("收到離開指令，正在關閉...");
                try { await host.StopAsync(); } catch { }
                return;
            case "/help":
                logger.LogInformation("可用指令: /exit, /quit, /ts, /help");
                continue;
            case "/ts":
                var tsMessage = $"Timestamp: {DateTimeOffset.UtcNow:O}";
                try
                {
                    await publisher.PublishAsync(tsMessage);
                    logger.LogInformation("已發送: {Msg}", tsMessage);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "發送時間戳訊息失敗");
                }
                continue;
        }

        try
        {
            await publisher.PublishAsync(line);
            logger.LogInformation("已發送: {Msg}", line);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "發送失敗");
        }
    }
}, cts.Token);

try
{
    await host.WaitForShutdownAsync();
}
catch (OperationCanceledException)
{
    // ignore
}
finally
{
    cts.Cancel();
    try { await inputTask; } catch { }
}
