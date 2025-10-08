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

var basePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppDomain.CurrentDomain.RelativeSearchPath ?? "");
string? credentialsPath = null;
if (!string.IsNullOrWhiteSpace(pubSubSettings.CredentialsPath))
    credentialsPath = Path.Combine(basePath, pubSubSettings.CredentialsPath);

// 設定 Emulator endpoint（僅在 UseEmulator = true 時設定）
string emulatorEndpoint = $"{emulatorSettings.Host}:{emulatorSettings.Port}";
if (pubSubSettings.UseEmulator)
{
    Environment.SetEnvironmentVariable("PUBSUB_EMULATOR_HOST", emulatorEndpoint);
    // 清掉可能殘留的正式憑證環境變數以避免干擾
    Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", null);
}
else
{
    // 確保不使用 emulator
    Environment.SetEnvironmentVariable("PUBSUB_EMULATOR_HOST", null);
    if (!string.IsNullOrWhiteSpace(credentialsPath))
    {
        Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", credentialsPath);
    }
}

// 根據 UseEmulator 選擇 EmulatorDetection 與憑證
var detection = pubSubSettings.UseEmulator ? EmulatorDetection.EmulatorOnly : EmulatorDetection.ProductionOnly;

// 低階 PublisherServiceApiClient (僅在需要時建立)
builder.Services.AddSingleton(provider =>
{
    var b = new PublisherServiceApiClientBuilder { EmulatorDetection = detection };
    if (!pubSubSettings.UseEmulator && !string.IsNullOrWhiteSpace(credentialsPath))
    {
        b.CredentialsPath = credentialsPath;
    }
    return b.Build();
});

// 高階 PublisherClient (可選) - 僅當設定啟用時才建構與使用
if (pubSubSettings.UseHighLevelPublisher)
{
    builder.Services.AddSingleton(async provider =>
    {
        var topicName = TopicName.FromProjectTopic(pubSubSettings.ProjectId, pubSubSettings.TopicId);
        var b = new PublisherClientBuilder
        {
            TopicName = topicName,
            EmulatorDetection = detection,
            Settings = new PublisherClient.Settings
            {
                BatchingSettings = new Google.Api.Gax.BatchingSettings(
                    elementCountThreshold: 100,
                    byteCountThreshold: 256 * 1024, // 256KB
                    delayThreshold: TimeSpan.FromMilliseconds(100))
            }
        };
        if (!pubSubSettings.UseEmulator && !string.IsNullOrWhiteSpace(credentialsPath))
        {
            b.CredentialsPath = credentialsPath;
        }
        return await b.BuildAsync();
    });
}

builder.Services.AddSingleton(provider =>
{
    var b = new SubscriberServiceApiClientBuilder { EmulatorDetection = detection };
    if (!pubSubSettings.UseEmulator && !string.IsNullOrWhiteSpace(credentialsPath))
    {
        b.CredentialsPath = credentialsPath;
    }
    return b.Build();
});

// 高階 SubscriberClient (StartAsync)
builder.Services.AddSingleton(provider =>
{
    var b = new SubscriberClientBuilder
    {
        SubscriptionName = SubscriptionName.FromProjectSubscription(pubSubSettings.ProjectId, pubSubSettings.SubscriptionId),
        EmulatorDetection = detection,
        Settings = new SubscriberClient.Settings
        {
            FlowControlSettings = new FlowControlSettings(
                maxOutstandingElementCount: 1,
                maxOutstandingByteCount: 10 * 1024 * 1024),
            // 定義 Ack 過期時間
            // - AckDeadline：預設 10 秒，最長 600 秒
            // - MaxTotalAckExtension: 預設 60 分鐘，最長受 Subscription 的 message retention 約束 (Subscription 預設 7 天) 
            AckDeadline = TimeSpan.FromSeconds(60),
            MaxTotalAckExtension = TimeSpan.FromMinutes(120),
        }
    };
    if (!pubSubSettings.UseEmulator && !string.IsNullOrWhiteSpace(credentialsPath))
    {
        b.CredentialsPath = credentialsPath;
    }
    return b.Build();
});

// Conditional IPubSubPublisher 實作註冊
if (pubSubSettings.UseHighLevelPublisher)
{
    // 將 async factory 解析為同步：利用 Task.GetAwaiter().GetResult()
    builder.Services.AddSingleton<IPubSubPublisher>(provider =>
    {
        var publisherClientTask = provider.GetRequiredService<Task<PublisherClient>>();
        var pc = publisherClientTask.GetAwaiter().GetResult();
        var logger = provider.GetRequiredService<ILogger<PubSubHighLevelPublisher>>();
        return new PubSubHighLevelPublisher(pc, logger);
    });
}
else
{
    builder.Services.AddSingleton<IPubSubPublisher, PubSubPublisher>();
}

// 註冊三種 Subscriber 背景服務
builder.Services.AddHostedService<PubSubSubscriber>();
builder.Services.AddHostedService<PubSubStreamingSubscriber>();
builder.Services.AddHostedService<PubSubStartAsyncSubscriber>();

builder.Services.AddSingleton<IPubSubBootstrap, PubSubBootstrap>();

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

if (pubSubSettings.UseEmulator)
    logger.LogInformation("Starting host with Pub/Sub emulator at {Endpoint}", emulatorEndpoint);
else
    logger.LogInformation("Starting host targeting GCP project {ProjectId} (production mode)", pubSubSettings.ProjectId);

// 啟動前確保 Topic / Subscription 存在 (Production 需要 GCP Pub/Sub 服務帳戶的 Viewer 權限，僅在 UseEmulator = true 時執行)
if (pubSubSettings.UseEmulator)
{
    var bootstrap = host.Services.GetRequiredService<IPubSubBootstrap>();
    await bootstrap.EnsureInfrastructureAsync();
}

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
