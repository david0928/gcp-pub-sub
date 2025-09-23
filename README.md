docker compose up -d --build
# GCP Pub/Sub .NET 8 Console Demo

一個使用 **.NET 8** 與 **Google.Cloud.PubSub.V1** 官方 SDK 的主控台範例，整合 **Pub/Sub Emulator (Docker)**，示範：

* 發送訊息 (Publish)
* 以背景服務長期拉取 (Pull) 訂閱並 Ack
* 啟動時自動建立 Topic / Subscription（若不存在）
* 單元測試 (NUnit + NSubstitute)

---
## 目錄
- [GCP Pub/Sub .NET 8 Console Demo](#gcp-pubsub-net-8-console-demo)
  - [目錄](#目錄)
  - [功能特色](#功能特色)
  - [架構概念](#架構概念)
  - [專案結構](#專案結構)
  - [環境需求](#環境需求)
  - [快速開始](#快速開始)
  - [執行與觀察](#執行與觀察)
  - [測試與覆蓋率](#測試與覆蓋率)
  - [設定說明](#設定說明)
  - [常見問題 FAQ](#常見問題-faq)
  - [延伸方向](#延伸方向)
  - [授權](#授權)

---
## 功能特色
* `PubSubBootstrap` 啟動時確保 Topic / Subscription 存在
* `PubSubPublisher` 發佈訊息（附加 `published_at` 屬性）
* `PubSubSubscriber` 背景服務以迴圈方式 Pull + Ack（簡化 emulator 範例）
* `PubSubStreamingSubscriber` 使用 StreamingPull 持續接收並即時 Ack
* Docker Compose 自動啟動 Emulator 並可直接測試
* 單元測試模擬 `PublisherServiceApiClient` 回傳 Publish Id

---
## 架構概念
文字架構示意：
```
┌────────────────────────┐        Publish        ┌──────────────────────────────┐
│ Console (Generic Host) │ ───────────────────▶ │  Pub/Sub (Emulator or GCP)   │
│                        │                      │  Topic: demo-topic           │
│  - PubSubBootstrap     │ ◀── Pull & Ack ───── │  Subscription: demo-sub      │
│  - PubSubPublisher     │                      └──────────────────────────────┘
│  - PubSubSubscriber    │  (Pull)
│  - PubSubStreamingSubscriber (StreamingPull)
└────────────────────────┘
```
流程：
1. 啟動 → 設定 `PUBSUB_EMULATOR_HOST`
2. Bootstrap：檢查 / 建立 Topic & Subscription
3. 發佈一則示範訊息
4. 背景服務開始持續 Pull → 解析 → Ack

---
## 專案結構
```
GcpPubSubDemo.sln
docker-compose.yml
emulator-init.sh
src/
	GcpPubSubDemo/
		Program.cs
		PubSubBootstrap.cs
		PubSubPublisher.cs
		PubSubSubscriber.cs
		AppSettings.cs
		appsettings.json
tests/
	GcpPubSubDemo.Tests/
		PublisherTests.cs
```

---
## 環境需求
* Docker / Docker Desktop
* .NET 8 SDK (`dotnet --version` 應顯示 8.x)
* (選用) `curl` 用於 emulator 健康檢查

---
## 快速開始
1. 啟動 Emulator：
	 ```bash
	 docker compose up -d
	 ```
2. 設定環境變數並執行：
	 ```bash
	 export PUBSUB_EMULATOR_HOST=localhost:8085
	 dotnet run --project src/GcpPubSubDemo/GcpPubSubDemo.csproj -c Release
	 ```
3. 看到輸出包含：`Created topic` / `Created subscription` / `Published message` / `Received message` 即成功。

Windows PowerShell：
```powershell
$Env:PUBSUB_EMULATOR_HOST = "localhost:8085"
dotnet run --project src/GcpPubSubDemo/GcpPubSubDemo.csproj -c Release
```

---
## 執行與觀察
再次發送訊息（重新執行即可，每次啟動都會 Publish 一則）：
```bash
export PUBSUB_EMULATOR_HOST=localhost:8085
dotnet run --project src/GcpPubSubDemo/GcpPubSubDemo.csproj
```

觀察 Emulator 日誌：
```bash
docker logs -f pubsub-emulator | head -n 50
```

---
## 測試與覆蓋率
執行單元測試：
```bash
dotnet test tests/GcpPubSubDemo.Tests/GcpPubSubDemo.Tests.csproj -c Release
```

收集覆蓋率 (
需已安裝 coverlet.collector)：
```bash
dotnet test tests/GcpPubSubDemo.Tests/GcpPubSubDemo.Tests.csproj \
	-c Release \
	--collect:"XPlat Code Coverage"
```
結果會輸出 `coverage.cobertura.xml`。

---
## 設定說明
`appsettings.json`：
```json
{
	"PubSub": {
		"ProjectId": "demo-project",
		"TopicId": "demo-topic",
		"SubscriptionId": "demo-sub"
	},
	"Emulator": {
		"Host": "localhost",
		"Port": 8085
	}
}
```
調整：
* 改變 Topic/Subscription 名稱後需刪除舊容器或重新建立資源
* 若要連真實 GCP：移除 `PUBSUB_EMULATOR_HOST`，並設定憑證 (`GOOGLE_APPLICATION_CREDENTIALS`)

---
## 常見問題 FAQ
**Q: Pull 與 StreamingPull 差異？**  
A: Pull 是「請求-回應」輪詢模式，適合簡單與低流量測試；StreamingPull 維持一條雙向串流，延遲更低且吞吐較佳，適合正式或高頻需求。

**Q: 執行時顯示 Topic not found?**  
A: 可能是在 bootstrap 之前就手動呼叫 Publisher，或 emulator 尚未初始化完成。請確認啟動順序與日誌。

**Q: 為什麼使用 Pull 而非 StreamingPull?**  
A: Emulator 測試情境簡化，Pull 實作更直覺。若需更高吞吐或低延遲可改 StreamingPull。

**Q: 可以在同一個行程內同時發佈與訂閱嗎?**  
A: 可以，目前程式啟動後即示範一次 Publish；可再加入互動 CLI 或排程。

**Q: 如何切換到正式 GCP?**  
A: 移除環境變數 `PUBSUB_EMULATOR_HOST`，並提供服務帳戶 JSON 憑證：
```bash
export GOOGLE_APPLICATION_CREDENTIALS=/path/key.json
```

---
## 延伸方向
* StreamingPull + Flow Control + 多工作執行緒處理
* 訊息重試 / 死信佇列 (Dead Letter Topic)
* Ordering Key / Exactly-once (需特定設定)
* JSON Schema 與驗證
* 整合 OpenTelemetry (Tracing / Metrics / Logging)
* 健康檢查 + Ready/Liveness Probe (若容器化)

---
## 授權
MIT License
