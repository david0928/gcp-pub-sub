# GCP Pub/Sub .NET 8 Console Demo

一個使用 **.NET 8** 與 **Google.Cloud.PubSub.V1** 官方 SDK 的主控台範例，整合 **Pub/Sub Emulator (Docker)**，示範：

* 發送訊息 (Publish)
* 以背景服務長期拉取 (Pull) 訂閱並 Ack
* 使用 StreamingPull 持續接收訊息
* 使用高階 API StartAsync 簡化訂閱邏輯
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
	- [PublisherClient 與 PublisherServiceApiClient 差異](#publisherclient-與-publisherserviceapiclient-差異)
	- [三種訂閱實作比較](#三種訂閱實作比較)
		- [1. Pull (`SubscriberServiceApiClient.Pull`)](#1-pull-subscriberserviceapiclientpull)
		- [2. StreamingPull（手寫串流 `StreamingPull`）](#2-streamingpull手寫串流-streamingpull)
		- [3. `SubscriberClient.StartAsync`（高階封裝）](#3-subscriberclientstartasync高階封裝)
		- [StartAsync 與手寫 StreamingPull 差異](#startasync-與手寫-streamingpull-差異)
		- [總結](#總結)
	- [延伸方向](#延伸方向)
	- [GCP IAM 權限 (Publisher / Subscriber / Bootstrap)](#gcp-iam-權限-publisher--subscriber--bootstrap)
	- [授權](#授權)

---
## 功能特色
* `PubSubBootstrap` 啟動時確保 Topic / Subscription 存在
* `PubSubPublisher` 發佈訊息（附加 `published_at` 屬性）
* `PubSubSubscriber` 背景服務以迴圈方式 Pull + Ack（簡化 emulator 範例）
* `PubSubStreamingSubscriber` 使用 StreamingPull 持續接收並即時 Ack
* `PubSubStartAsyncSubscriber` 使用高階 API StartAsync，簡化訂閱邏輯與流量控制
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
│  - PubSubStartAsyncSubscriber (StartAsync)    │
└────────────────────────┘
```
流程：
1. 啟動 → 設定 `PUBSUB_EMULATOR_HOST`
2. Bootstrap：檢查 / 建立 Topic & Subscription
3. 發佈一則示範訊息
4. 背景服務開始持續 Pull 或 StreamingPull 或 StartAsync → 解析 → Ack

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
		PubSubHighLevelPublisher.cs
		PubSubPublisher.cs
		PubSubStartAsyncSubscriber.cs
		PubSubStreamingSubscriber.cs
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
docker compose up -d --build
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

收集覆蓋率（需已安裝 coverlet.collector）：
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
		"SubscriptionId": "demo-sub",
		"UseEmulator": true,             // true = 使用本機 emulator；false = 連正式 GCP
		"CredentialsPath": null,         // 當 UseEmulator=false 且提供路徑時，使用該 service account JSON；為 null 則走 ADC
    	"UseHighLevelPublisher": false	 // 是否使用高階 PublisherClient (具備 batching / 背景 flush)。false 則使用低階 PublisherServiceApiClient
	},
	"Emulator": {
		"Host": "localhost",
		"Port": 8085
	}
}
```

行為說明：
* 改變 Topic/Subscription 名稱後需刪除舊容器或重新建立資源
* UseEmulator=true：設定 `PUBSUB_EMULATOR_HOST`，並清除 `GOOGLE_APPLICATION_CREDENTIALS`。
* UseEmulator=false：清除 `PUBSUB_EMULATOR_HOST`；若 `CredentialsPath` 有值則設定 `GOOGLE_APPLICATION_CREDENTIALS`，否則依 Application Default Credentials (ADC) 尋找（例如 gcloud login 或執行環境提供的 metadata）。
* 三種 Subscriber / Publisher 都共用相同偵測邏輯 (`EmulatorDetection = EmulatorOnly / ProductionOnly`)。

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
## PublisherClient 與 PublisherServiceApiClient 差異
兩者關係類似 StartAsync(高階) 與 手寫 StreamingPull(低階) 的概念，但作用在「發佈」。  
`PublisherServiceApiClient` = 直接呼叫 Publish RPC；`PublisherClient` = 高階封裝，提供批次、併發與背景排程。

差異重點:
* 抽象層級:
  * PublisherServiceApiClient：一呼叫一次 Publish RPC；無額外排程。
  * PublisherClient：內建背景批次器、併發與緩衝。
* Batching:
  * PublisherServiceApiClient：需自行分批與控制訊息大小 (1MB 限制)。
  * PublisherClient：自動依大小/訊息數/時間門檻切批 (可設定 BatchSettings)。
* Flow Control / 背壓:
  * PublisherServiceApiClient：無；你送多快就打多快，易觸發資源壓力。
  * PublisherClient：可設定 MaxOutstandingElementCount / MaxOutstandingByteCount；超過可阻塞或擲出例外。
* 重試與 Backoff:
  * PublisherServiceApiClient：需自行包裝 Polly / 重試邏輯。
  * PublisherClient：內建暫時性錯誤重試與指數退避。
* Ordering Key:
  * PublisherServiceApiClient：需自行確保同一 ordering key 序列化送出。
  * PublisherClient：支援 Ordering；失敗時可停用/恢復指定 key。
* 併發與 Thread Safety:
  * PublisherServiceApiClient：可重用實例，但沒有幫你分散 Publish 並行。
  * PublisherClient：執行緒安全；內部排程多個 RPC 並平衡吞吐。
* 效能 / 延遲:
  * PublisherServiceApiClient：單筆延遲低（無等待批次），大量高 TPS 時 RPC 過多。
  * PublisherClient：平均延遲稍高（等待批次觸發），但高吞吐下整體資源效率佳。
* 記憶體與緩衝:
  * PublisherServiceApiClient：無內建緩衝；壓力直達網路與伺服端。
  * PublisherClient：有暫存佇列；需留意過大批次設定造成記憶體佔用。
* 錯誤處理:
  * PublisherServiceApiClient：錯誤即時回傳；自行分類重試 / 失敗紀錄。
  * PublisherClient：背景重試；最終失敗會從 Task 例外呈現（需觀察）。
* 觀測性:
  * PublisherServiceApiClient：易追蹤呼叫次數，但高頻時雜訊多。
  * PublisherClient：需針對批次結果與佇列長度自加指標。
* 適用情境:
  * PublisherServiceApiClient：Bootstrap (建 Topic)、低量事件、測試、對延遲極敏感單筆操作。
  * PublisherClient：正式高吞吐、多併發、大量小訊息、需自動批次 / 重試 / 流控。
* 切換考量:
  * 若現在用 PublisherServiceApiClient 且 TPS 上升或 Publish RPC 數過高 → 改用 PublisherClient。
  * 若為稀疏事件（1 秒幾筆）不一定要換。

簡短範例（省略錯誤處理）:
```csharp
// 低階：直接呼叫
var service = await PublisherServiceApiClient.CreateAsync();
await service.PublishAsync(topicName, new[] { PubsubMessage.FromData(data) });

// 高階：具批次、重試
var publisher = await PublisherClient.CreateAsync(topicName);
await publisher.PublishAsync(data);
```

建議:
* 立即追求穩定吞吐 → 直接選 PublisherClient。
* 先行 PoC / 測試腳本 / 初始化腳本 → 可用 PublisherServiceApiClient；日後再抽換。

---
## 三種訂閱實作比較

本範例同時示範三種接收訊息方式：`PubSubSubscriber` (Pull) / `PubSubStreamingSubscriber` (StreamingPull) / `PubSubStartAsyncSubscriber` (`SubscriberClient.StartAsync`)。以下比較其差異：

### 1. Pull (`SubscriberServiceApiClient.Pull`)
優點：
* 實作最直覺、邏輯可見（便於教學 / 除錯）
* 可精準控制每次拉取數量（例如設定 `MaxMessages`）
* 沒有長連線；對於非常低流量或偶發測試足夠
缺點：
* 延遲較高（輪詢間隔 + RPC 往返）
* 吞吐量受限（大量訊息時會造成頻繁 RPC）
* 需自行處理 backoff、錯誤重試、Ack/Nack
* 不適合高頻或需要低延遲的生產情境

適用情境：PoC / 教學 / Emulator 測試 / 低量非即時。

### 2. StreamingPull（手寫串流 `StreamingPull`）
優點：
* 單一長連線，低延遲、高吞吐
* 可批次 Ack，降低 RPC 開銷
* 可自行擴充 flow control、並行處理設計
缺點：
* 需管理串流生命週期、錯誤恢復、重連
* 實作複雜度較高，錯誤處理細節多
* 容易忽略流量控制或 Ack 超時的細節

適用情境：需要較低延遲或中高吞吐且希望完全掌控行為的服務。

### 3. `SubscriberClient.StartAsync`（高階封裝）
優點：
* 官方建議的高階 API，內建串流管理 / 自動重連 / 流量控制
* 以 callback 形式處理訊息；回傳 Ack / Nack 即可
* 需要的程式碼最少，可快速上線
* 預設並行度 + 可調整設定（`SubscriberClient.Settings` / `SubscriptionSettings` 等）
缺點：
* 彈性比手寫 StreamingPull 低（某些細節參數無法完全操控）
* Callback 內長時間阻塞會影響吞吐（需自行分派工作或使用非同步 I/O）
* 除錯時需要熟悉內部抽象；較難觀察底層 Streaming 狀態

適用情境：大多數實務生產服務（除非有極端特殊需求需要手寫 StreamingPull）。

### StartAsync 與手寫 StreamingPull 差異
重點對照（StartAsync 其實就是對 StreamingPull 的高階封裝）：
* 抽象層級：
  * StartAsync：框架管理底層 StreamingPull 生命週期（建立、錯誤、重連、關閉）。
  * 手寫 StreamingPull：需自行維護雙向串流與讀寫協調。
* Ack Deadline / Lease Extension：
  * StartAsync：自動延展未 Ack 訊息的期限（避免過早重送）。
  * 手寫：若處理時間較長需自行實作 ModifyAckDeadline / 週期續約。
* Flow Control（流量控制）：
  * StartAsync：可透過 SubscriberClient.Settings 設定 MaxOutstandingElementCount / MaxOutstandingByteCount。
  * 手寫：需自行實作本地佇列、背壓（Backpressure）、暫停拉取策略。
* 重試與重連：
  * StartAsync：內建 transient error backoff（含 jitter）。
  * 手寫：需自行辨識可重試錯誤、決定 backoff 策略與最大重試。
* 併發處理模型：
  * StartAsync：Callback 可能並行執行（受限於設定）；長時間 I/O 建議再排到工作隊列。
  * 手寫：完全由你決定並行度（Task.Run、Channel、TPL Dataflow 等）。
* Ack 模式：
  * StartAsync：回傳 Ack / Nack；簡化語意。
  * 手寫：收集 AckId 後批次送 Acknowledge；可自訂批次大小 / 時間。
* 觀測性：
  * StartAsync：底層細節被抽象；需透過外層計時/計數或自訂 Logger。
  * 手寫：可在每個 StreamRead / StreamWrite / Ack 批次點插入 Metrics / Tracing。
* 最佳化空間：
  * StartAsync：足以滿足 90% 一般後端需求。
  * 手寫：適合需要極致低延遲、客製批次策略、動態流控、精細錯誤分類。
* 心智負擔：
  * StartAsync：低；專注於業務處理。
  * 手寫：高；需理解 Pub/Sub lease / flow control / streaming 行為。
適用建議：
* 先用 StartAsync 做 MVP 或直接上線；只有在出現吞吐瓶頸、流量尖峰控制或特殊 Ack 時序需求再考慮手寫。
* 觀測到 Ack 逾時或處理阻塞 → 優先優化 callback（非同步化 / 工作分派）再評估改寫 StreamingPull。
效能觀念：
* 在大多數中等流量（每秒數百～數千訊息）情境下，StartAsync 與最佳化手寫 StreamingPull 差異可忽略。
* 真正的瓶頸常在下游處理（DB / 外部 API）；先度量再重構。

### 總結
若只是要「穩定收訊息」且沒有太特殊需求，直接使用 `SubscriberClient.StartAsync` 即可；Pull 適合學習與快速驗證；StreamingPull 手寫版保留最大彈性，適用需要深入優化或觀測的情境。



---
## 延伸方向
* StreamingPull + Flow Control + 多工作執行緒處理
* 訊息重試 / 死信佇列 (Dead Letter Topic)
* Ordering Key / Exactly-once (需特定設定)
* JSON Schema 與驗證
* 整合 OpenTelemetry (Tracing / Metrics / Logging)
* 健康檢查 + Ready/Liveness Probe (若容器化)

---
## GCP IAM 權限 (Publisher / Subscriber / Bootstrap)

在連接正式 GCP 時，建議為不同職能的 service account 授予最小必要權限。以下為範例預設角色與必要權限說明，亦提供 gcloud 指令範例。

注意：使用 Pub/Sub Emulator 時不需要這些 IAM 權限。

- Publisher（發佈訊息）
  - 建議角色：roles/pubsub.publisher
  - 主要權限：pubsub.topics.publish（允許將訊息發佈到 Topic）
  - gcloud 範例：
    ```bash
    gcloud projects add-iam-policy-binding YOUR_PROJECT_ID \
      --member="serviceAccount:YOUR_PUBLISHER_SA@YOUR_PROJECT_ID.iam.gserviceaccount.com" \
      --role="roles/pubsub.publisher"
    ```

- Subscriber（拉取並 Ack 訊息）
  - 建議角色：roles/pubsub.subscriber
  - 主要權限：pubsub.subscriptions.consume / pubsub.subscriptions.pull / pubsub.subscriptions.acknowledge（允許 pull、ack/nack）
  - gcloud 範例：
    ```bash
    gcloud projects add-iam-policy-binding YOUR_PROJECT_ID \
      --member="serviceAccount:YOUR_SUBSCRIBER_SA@YOUR_PROJECT_ID.iam.gserviceaccount.com" \
      --role="roles/pubsub.subscriber"
    ```

- PubSubBootstrap（建立 / 檢查 Topic 與 Subscription）
  - 建議角色（部署階段或 bootstrap 專用）：roles/pubsub.admin
  - 主要權限（可建立或修改 Topic / Subscription）：pubsub.topics.create, pubsub.subscriptions.create, pubsub.topics.get, pubsub.subscriptions.get 等
  - 若不希望賦予完整 admin，可建立自訂角色，僅包含：
    - pubsub.topics.create
    - pubsub.subscriptions.create
    - pubsub.topics.get
    - pubsub.subscriptions.get
  - gcloud 範例（臨時授權或部署帳號）：
    ```bash
    gcloud projects add-iam-policy-binding YOUR_PROJECT_ID \
      --member="serviceAccount:YOUR_BOOTSTRAP_SA@YOUR_PROJECT_ID.iam.gserviceaccount.com" \
      --role="roles/pubsub.admin"
    ```

- 僅檢查 Topic / Subscription 是否存在
  - 若僅需檢查資源存在性（例如 bootstrap 只做 get），可授予較低權限：roles/pubsub.viewer
  - 主要權限範例：pubsub.topics.get、pubsub.subscriptions.get（不包含建立或發佈/消費權限）
  - gcloud 範例：
    ```bash
    gcloud projects add-iam-policy-binding YOUR_PROJECT_ID \
      --member="serviceAccount:YOUR_VIEWER_SA@YOUR_PROJECT_ID.iam.gserviceaccount.com" \
      --role="roles/pubsub.viewer"
    ```

小提醒：
- 若希望更嚴格的最小權限，考慮為 bootstrap 階段使用單獨的 SA（只在部署時授權），運行後撤銷或只賦予必要的 create/get 權限。
- 在生產環境建議使用不同 service account／角色來分離發佈與消費職責，避免單一帳號擁有過多權限。
- 若使用 ADC（Application Default Credentials）或在 GKE / Cloud Run 等環境，請確保執行環境的 Compute Service Account 也被授予相應角色。

---
## 授權
MIT License