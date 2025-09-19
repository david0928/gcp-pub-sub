using GcpPubSubDemo;
using Google.Cloud.PubSub.V1;
using Grpc.Net.Client;
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
    EmulatorDetection = Google.Api.Gax.EmulatorDetection.EmulatorOnly
}.Build());

builder.Services.AddSingleton(provider => new SubscriberServiceApiClientBuilder
{
    EmulatorDetection = Google.Api.Gax.EmulatorDetection.EmulatorOnly
}.Build());

builder.Services.AddSingleton<IPubSubPublisher, PubSubPublisher>();
builder.Services.AddHostedService<PubSubSubscriber>();
builder.Services.AddSingleton<IPubSubBootstrap, PubSubBootstrap>();

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

logger.LogInformation("Starting host with Pub/Sub emulator at {Endpoint}", emulatorEndpoint);

// 啟動前確保 Topic / Subscription 存在
var bootstrap = host.Services.GetRequiredService<IPubSubBootstrap>();
await bootstrap.EnsureInfrastructureAsync();

await host.StartAsync();

// 簡單示範發送一則訊息
var publisher = host.Services.GetRequiredService<IPubSubPublisher>();
await publisher.PublishAsync($"Hello Pub/Sub @ {DateTimeOffset.UtcNow:O}");

logger.LogInformation("Press Ctrl+C to exit...");
try
{
    await host.WaitForShutdownAsync();
}
catch (OperationCanceledException)
{
    // ignore
}
