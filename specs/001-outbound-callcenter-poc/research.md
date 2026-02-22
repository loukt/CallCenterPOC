# Research: Outbound Call Center POC

**Feature**: `001-outbound-callcenter-poc`  
**Date**: 2026-02-20  
**Purpose**: Resolve all NEEDS CLARIFICATION items and document technology decisions

## 1. Testing Framework

**Decision**: xUnit with `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory<Program>`) for contract tests, Moq for mocking.

**Rationale**: xUnit is the default ASP.NET Core testing framework — all Microsoft docs and templates use it. `WebApplicationFactory` provides full in-memory HTTP pipeline testing without a network port. xUnit enforces test isolation by default (new instance per test), which is important when testing `CallService` with its in-memory dictionaries.

**Alternatives considered**:
- NUnit: Mature but less idiomatic for ASP.NET Core; `[SetUp]`/`[TearDown]` lifecycle is more implicit.
- MSTest: Microsoft-owned but fewer community examples for ASP.NET Core integration testing.

**NuGet packages**: `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `Microsoft.AspNetCore.Mvc.Testing`, `Moq`, `coverlet.collector`

## 2. ACS Call Automation — Outbound Call Patterns

**Decision**: Event-driven callback model with `CallAutomationEventParser.Parse()` in the callback endpoint. Do NOT use `WaitForEventProcessorAsync()` in production paths (it blocks threads).

**Rationale**: ACS Call Automation is event-driven (POSTs `CloudEvent[]` to callback URI). The codebase already uses this correctly. `WaitForEventProcessorAsync()` (used in `StartCallInteractionToPlaySound`) ties up threads and is incompatible with concurrent call handling.

**Key findings**:
- Recording: Start on `CallConnected` via `StartRecordingOptions` with `ServerCallLocator`. Stop on `CallDisconnected` via `StopAsync(recordingId)`.
- WebSocket buffer: Current 2048-byte buffer may truncate audio frames. Increase to 4096+ bytes or implement message reassembly (check `EndOfMessage`).
- Thread safety: `_acsMediaStreamingHandler` is a single field on a Singleton `CallService` — concurrent calls overwrite each other. Must use `ConcurrentDictionary<string, AcsMediaStreamingHandler>`.
- `startMediaStreaming: true` is correctly set for immediate streaming on connect.

## 3. Azure OpenAI Realtime API — Session Management

**Decision**: One `RealtimeConversationSession` per call. PCM 16-bit 24 kHz mono for both input/output. Server-side VAD for barge-in.

**Rationale**: Sessions are stateful and must not be shared. ACS `Pcm24KMono` (24 kHz/16-bit) matches the Realtime API's `Pcm16` format — no transcoding needed. Server VAD handles turn-taking automatically.

**Key findings**:
- Barge-in: `ConversationInputSpeechStartedUpdate` fires → send `StopAudioForOutbound` to ACS → API cancels current response and processes new input. Already implemented correctly.
- VAD settings: `0.5f` threshold, `500ms` prefix/silence are reasonable defaults. Increase silence to 700-800ms if AI responds too eagerly.
- `StartResponseAsync()` triggers AI greeting without user input — correct for this use case.
- `AzureOpenAIClient` can be shared as singleton (stateless), but per-call creation is acceptable for POC scale.

## 4. Live Transcript Streaming to Frontend

**Decision**: SignalR (ASP.NET Core built-in).

**Rationale**: First-class ASP.NET Core library, no additional server NuGet needed. Handles reconnection, fallback transports, bidirectional communication. The backend already has transcript data from `ConversationItemStreamingAudioTranscriptionFinishedUpdate` (AI) and `ConversationInputTranscriptionFinishedUpdate` (user).

**Pattern**: `TranscriptHub` class on API. Inject `IHubContext<TranscriptHub>` into service layer. Push transcript fragments via `Clients.Group(callConnectionId).SendAsync("TranscriptUpdate", ...)`. Frontend JS connects to API hub URL directly (requires CORS).

**Alternatives considered**:
- SSE: Simpler but unidirectional; no built-in reconnection in ASP.NET Core.
- Polling: Adds latency; feels laggy for live transcript.
- Raw WebSocket: Re-implements what SignalR provides for free.

## 5. Call Duration Enforcement

**Decision**: `CancellationTokenSource` with `CancelAfter(TimeSpan.FromMinutes(5))` scoped per call.

**Rationale**: Idiomatic .NET pattern. Integrates with existing `CancellationTokenSource` in `AcsMediaStreamingHandler` and `AzureOpenAIService`. When cancelled: call `CallConnection.HangUpAsync(forEveryone: true)` → ACS fires `CallDisconnected` → existing cleanup runs. Register `cts.Token.Register(...)` for proactive hang-up.

**Alternatives considered**:
- System.Threading.Timer: Separate scheduling mechanism; more complex disposal and thread-safety.
- ACS-level timeout: Not supported on `CreateCallOptions`.
- BackgroundService with timer queue: Over-engineered for 5-call POC cap.

---

## Phase 2 Research (Added 2026-02-21)

### 6. Blob Storage JSON Persistence (Campaigns & Call History)

**Decision**: Single JSON file for campaigns (`campaigns.json`), one JSON file per call record (`call-history/{callConnectionId}.json`).

**Rationale**: Campaigns are a small collection (tens at most) — a single file with write-through is simplest. Call records grow unboundedly, so one-file-per-record avoids rewriting a growing monolithic file, keeps reads O(1) by ID, and allows listing via blob prefix enumeration.

**Pattern**: Inject `BlobServiceClient` via DI. Use `DefaultAzureCredential` + storage account URI for Azure deployment (consistent with existing Managed Identity pattern). Support `BlobStorage:ConnectionString` as a local development fallback. Container name is configurable (default: `callcenter-data`).

**Alternatives considered**:
- Azure Table Storage: Mentioned in spec clarifications, but Blob JSON is simpler (no schema, no partition key design), and the data volume is trivially small for a POC.
- SQLite: Would require file system access and migration tooling — over-engineered for POC.
- In-memory only: Doesn't survive restarts — user explicitly requested persistence.

### 7. Sentiment Analysis via Azure OpenAI Chat Completion

**Decision**: Use Azure OpenAI GPT chat completion (gpt-4o-mini deployment) with a classification prompt for per-segment sentiment.

**Rationale**: The system already uses Azure OpenAI — adding a chat completion call reuses existing credentials and infrastructure. A lightweight model (gpt-4o-mini) keeps latency low (<500ms per segment) and cost minimal. The prompt instructs the model to return a JSON object `{"label":"Positive|Neutral|Negative","confidence":0.X}`.

**Latency expectations**: SC-009 requires sentiment labels within 1 second of transcript receipt. gpt-4o-mini typically responds in 200-500ms for short classification prompts. Sentiment is delivered asynchronously via a dedicated `SentimentUpdate` SignalR event to avoid blocking transcript delivery.

**Auth pattern**: Same as `AzureOpenAIService` — API key for local dev (via `AzureOpenAI:Key`), `DefaultAzureCredential` for Azure deployment. Deployment name configured via `AzureOpenAI:ChatDeployment` (separate from the Realtime model deployment).

**Alternatives considered**:
- Azure AI Language (sentiment analysis API): Purpose-built but adds a new Azure service dependency, separate credentials, and SDK. Over-engineered for POC.
- Client-side heuristic: Unreliable for nuanced sentiment; defeats the purpose of demonstrating AI capability.

### 8. Multi-Call Orchestration (Parallel CreateCallAsync)

**Decision**: Sequential `CreateCallAsync()` calls in a loop (not `Task.WhenAll`), with per-call error handling.

**Rationale**: With a maximum of 2 calls, parallelism provides negligible benefit. Sequential calls simplify error handling: if the first call fails, the second is not attempted, and the user gets a clear error. If the first succeeds but the second fails, the first call continues (it's already placed — the user can hang it up). Each call gets its own `ActiveCall` entry, WebSocket, and AI session from the start.

**Error handling**: If `CreateCallAsync` throws for one number, catch the exception, skip that number, and include the error in the response alongside successful calls. The `CallInitiatedResponse.calls` array reflects only successfully placed calls. If all calls fail, return 500.

**Alternatives considered**:
- `Task.WhenAll`: Would place calls truly simultaneously but complicates partial failure handling. For 2 calls, the latency difference is negligible (<100ms).
- Transaction-like rollback: If the second call fails, hang up the first. Rejected — the first call is already valuable and shouldn't be cancelled due to an unrelated number's failure.
