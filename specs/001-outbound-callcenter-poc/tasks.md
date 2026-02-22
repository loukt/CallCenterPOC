# Tasks: Outbound Call Center POC

**Input**: Design documents from `/specs/001-outbound-callcenter-poc/`  
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/api.yaml, quickstart.md

**Tests**: Included — Constitution Principle V requires unit tests for validation logic and contract tests for HTTP endpoints.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3, US4)
- All file paths are relative to the repository root

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create test project and add required client libraries

- [x] T001 Create xUnit test project `CallCenterPOC-API.Tests/CallCenterPOC-API.Tests.csproj` with NuGet packages: `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `Microsoft.AspNetCore.Mvc.Testing`, `Moq`, `coverlet.collector`. Add project reference to `ContactCenterPOC-API/ContactCenterPOC-API.csproj`. Add project to `CallCenterPOC.sln`. Also append `public partial class Program { }` to the bottom of `ContactCenterPOC-API/Program.cs` so `WebApplicationFactory<Program>` can discover the entry point (required for T016 contract tests to compile).
- [x] T002 [P] Add `@microsoft/signalr` JavaScript client library to `CallCenterPOC-App/wwwroot/lib/microsoft-signalr/` (via LibMan or CDN download). This is the browser-side SignalR client needed for live transcript streaming.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core data models, thread-safe refactoring, SignalR hub, and CORS — MUST be complete before any user story work begins

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [x] T003 [P] Define `CallStatus` enum (`Initiating`, `Ringing`, `Connected`, `Disconnected`) and `ActiveCall` class (`CallConnectionId`, `ServerCallId?`, `TargetPhoneNumber`, `Prompt`, `Status`, `StartedAt`, `RecordingId?`, `CancellationTokenSource`) in `ContactCenterPOC-API/Models/ActiveCall.cs` per data-model.md §2. Note: `ServerCallId` is `string?` — null until the `CallConnected` event populates it (see T013).
- [x] T004 [P] Define `TranscriptEntry` class (`CallConnectionId`, `Speaker`, `Text`, `Timestamp`) and `SpeakerType` enum (`AI`, `Recipient`) in `ContactCenterPOC-API/Models/TranscriptEntry.cs` per data-model.md §4
- [x] T005 [P] Define `CallStatusUpdate` class (`CallConnectionId`, `Status`, `Message?`, `Timestamp`) in `ContactCenterPOC-API/Models/CallStatusUpdate.cs` per data-model.md §5
- [x] T006 Refactor `CallService` in `ContactCenterPOC-API/Services/CallService.cs`: replace the four separate `Dictionary` fields (`_activeConnections`, `_activeCallPrompt`, `_activeCallNumbers`, `_activeRecordings`) and the single `_acsMediaStreamingHandler` field with a single `ConcurrentDictionary<string, ActiveCall>` and a `ConcurrentDictionary<string, AcsMediaStreamingHandler>` keyed by `callConnectionId`. Update all methods to use the new dictionaries. This fixes the thread-safety issue identified in research.md §2.
- [x] T007 [P] Create `TranscriptHub` SignalR hub class in `ContactCenterPOC-API/Hubs/TranscriptHub.cs`. The hub should support client group join/leave by `callConnectionId` (so the frontend subscribes to updates for a specific call). Define methods: `JoinCall(string callConnectionId)`, `LeaveCall(string callConnectionId)`. Server-to-client events: `TranscriptUpdate(TranscriptEntry)`, `CallStatusChanged(CallStatusUpdate)`. Per research.md §4.
- [x] T008 Register SignalR services, CORS policy, and map the `/transcriptHub` endpoint in `ContactCenterPOC-API/Program.cs`. Add `builder.Services.AddSignalR()`, `builder.Services.AddCors(...)` allowing the origin from the `FrontendOrigin` config key (e.g., `https://localhost:5002`), `app.UseCors(...)`, and `app.MapHub<TranscriptHub>("/transcriptHub")`. The CORS policy must include `.AllowCredentials()` so SignalR can use its WebSocket transport instead of falling back to long-polling.
- [x] T009 [P] Fix WebSocket receive buffer in `ContactCenterPOC-API/Models/acsMediaStreamingHandler.cs`: increase from 2048 bytes to 4096 bytes and implement proper message reassembly by checking `receiveResult.EndOfMessage` before processing. Per research.md §2.

**Checkpoint**: Foundation ready — thread-safe CallService, data models, SignalR hub, and CORS all in place. User story implementation can now begin.

---

## Phase 3: User Story 3 — Real-Time Bidirectional Voice Conversation (Priority: P1)

**Goal**: Extract live transcript from the AI session and stream it to the operator via SignalR. The existing bidirectional audio bridge and barge-in already work — this phase adds transcript visibility.

**Independent Test**: Place a call, speak to the AI, verify transcript entries appear on the connected SignalR client for both AI and recipient speech, and confirm barge-in still works.

### Implementation for User Story 3

- [x] T010 [US3] Refactor `AcsMediaStreamingHandler` constructor in `ContactCenterPOC-API/Models/acsMediaStreamingHandler.cs` to accept `IHubContext<TranscriptHub>` and the `callConnectionId`. Store both as fields. The hub context will be used to push transcript entries.
- [x] T011 [US3] Refactor `AzureOpenAIService` in `ContactCenterPOC-API/Services/AzureOpenAIService.cs` to accept `IHubContext<TranscriptHub>` and `callConnectionId`. In `GetOpenAiStreamResponseAsync()`: on `ConversationItemStreamingAudioTranscriptionFinishedUpdate`, create a `TranscriptEntry` with `Speaker = SpeakerType.AI` and send it via `hubContext.Clients.Group(callConnectionId).SendAsync("TranscriptUpdate", entry)`. On `ConversationInputTranscriptionFinishedUpdate`, create a `TranscriptEntry` with `Speaker = SpeakerType.Recipient` and send similarly.
- [x] T012 [US3] Update `CallService.StartCallInteraction()` in `ContactCenterPOC-API/Services/CallService.cs` to pass `IHubContext<TranscriptHub>` and `callConnectionId` through to `AcsMediaStreamingHandler` and `AzureOpenAIService`. Inject `IHubContext<TranscriptHub>` into `CallService` constructor.
- [x] T013 [US3] Push `CallStatusUpdate` via SignalR in `CallbackController.CallbackEvent()` in `ContactCenterPOC-API/Controllers/CallbackController.cs`: on `CallConnected` → send status `Connected`, and also update the `ActiveCall` entry's `ServerCallId` from `@event.ServerCallId` (needed for recording in T028); on `CallDisconnected` → send status `Disconnected`. Inject `IHubContext<TranscriptHub>` into `CallbackController`.

**Checkpoint**: At this point, the AI-powered bidirectional voice conversation works and transcript + status updates are streamed via SignalR. No frontend consumption yet — that comes in US1.

---

## Phase 4: User Story 1 — Initiate an Outbound AI Call (Priority: P1) 🎯 MVP

**Goal**: Operator can initiate a validated call, see live status + transcript, hang up, and the system enforces concurrent/duration limits.

**Independent Test**: Enter a valid phone number and prompt, click "Make a Call," answer on a real phone, verify the AI speaks, transcript appears in the UI, click "Hang Up" to terminate. Verify invalid numbers are rejected, 6th concurrent call is rejected, and calls auto-terminate at 5 minutes.

### Tests for User Story 1

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [x] T014 [P] [US1] Write `PhoneNumberValidationTests` in `CallCenterPOC-API.Tests/Unit/PhoneNumberValidationTests.cs`: test E.164 validation — valid numbers (`+6591234567`, `+14155551234`), invalid numbers (`6591234567`, `+0123`, `abc`, empty string, null). Validate against the `[RegularExpression]` attribute on `CallRequest.PhoneNumber`.
- [x] T015 [P] [US1] Write `ConcurrentCallLimitTests` in `CallCenterPOC-API.Tests/Unit/ConcurrentCallLimitTests.cs`: test that `CallService` rejects the 6th concurrent call. Mock `CallAutomationClient`. Add 5 entries to the `ConcurrentDictionary<string, ActiveCall>`, then verify `InitiateCall` throws or returns an error.
- [x] T016 [P] [US1] Write `CallControllerContractTests` in `CallCenterPOC-API.Tests/Contract/CallControllerContractTests.cs`: use `WebApplicationFactory<Program>` to test: (a) `POST /api/Call/initiate` with invalid phone → 400, (b) `POST /api/Call/initiate` with missing body → 400, (c) `GET /api/Call/active` → 200 with expected JSON shape, (d) `POST /api/Call/hangup/nonexistent` → 404.

### Implementation for User Story 1

- [x] T017 [US1] Add `[Required]` and `[RegularExpression(@"^\+[1-9]\d{1,14}$")]` validation attributes to `CallRequest.PhoneNumber` in `ContactCenterPOC-API/Models/CallbackEventModels.cs`. Add `[JsonProperty]` for consistent casing. This implements FR-002.
- [x] T018 [US1] Add concurrent call limit check to `CallService.InitiateCall()` in `ContactCenterPOC-API/Services/CallService.cs`: if `_activeCalls.Count >= 5`, throw an `InvalidOperationException` or return a result indicating the limit is reached. This implements FR-017.
- [x] T019 [US1] Add per-call 5-minute timeout in `CallService.InitiateCall()` in `ContactCenterPOC-API/Services/CallService.cs`: create a `CancellationTokenSource`, call `cts.CancelAfter(TimeSpan.FromMinutes(5))`, store it on the `ActiveCall`, and register `cts.Token.Register(async () => { await HangUpCall(callConnectionId); })` to auto-terminate. This implements FR-016. Per research.md §5.
- [x] T020 [P] [US1] Implement `POST /api/Call/hangup/{callConnectionId}` action in `ContactCenterPOC-API/Controllers/CallController.cs`: look up the active call, call `CallConnection.HangUpAsync(forEveryone: true)`, stop recording, clean up resources, return 200. Return 404 if call not found. Per contracts/api.yaml. This implements FR-015.
- [x] T021 [P] [US1] Implement `GET /api/Call/active` action in `ContactCenterPOC-API/Controllers/CallController.cs`: return `ActiveCallsResponse` with count, maxConcurrent (5), and list of `ActiveCallSummary` objects from the `ConcurrentDictionary`. Per contracts/api.yaml.
- [x] T022 [US1] Update `POST /api/Call/initiate` in `ContactCenterPOC-API/Controllers/CallController.cs`: add `ModelState` validation (return 400 with `ErrorResponse` for invalid input), catch concurrent limit exceeded (return 429 with `ErrorResponse`), and return structured `CallInitiatedResponse` with `callConnectionId` (not `callId`) per contracts/api.yaml. Also update existing code's anonymous response object from `CallId` to `CallConnectionId` for consistency. This implements FR-012.
- [x] T023 [US1] Redesign `CallCenterPOC-App/Pages/Index.cshtml`: add a "Call Status" panel below the form showing: status indicator (badge), live transcript area (scrollable div), and a "Hang Up" button. The transcript area displays `TranscriptEntry` items with speaker labels. The hang-up button posts to `/api/Call/hangup/{id}`. Hide the panel when no call is active. This implements FR-014.
- [x] T024 [US1] Implement SignalR client logic in `CallCenterPOC-App/wwwroot/js/site.js`: connect to `{ApiBaseUrl}/transcriptHub`, join call group on successful initiation, handle `TranscriptUpdate` (append to transcript div) and `CallStatusChanged` (update status badge, show/hide hang-up button). Handle connection errors gracefully. This implements FR-014.
- [x] T025 [US1] Update `IndexModel.OnPostAsync()` in `CallCenterPOC-App/Pages/Index.cshtml.cs` to: (a) capture the `callConnectionId` from the API response and pass it to the page (via `ViewData` or `TempData`) for SignalR group join, (b) handle 429 responses with a user-friendly "concurrent limit reached" message, (c) handle 400 responses with validation error display.

**Checkpoint**: At this point, the operator can initiate a validated call, see live status + transcript, and hang up. Concurrent and duration limits are enforced. User Story 1 is fully functional and independently testable.

---

## Phase 5: User Story 2 — Select a Pre-Defined Prompt Scenario (Priority: P2)

**Goal**: Pre-defined prompt scenarios work correctly with visual selection state, and empty prompts fall back to a default.

**Independent Test**: Click each pre-defined scenario, verify the textarea is populated. Edit the text, initiate a call, confirm the AI uses the modified prompt. Submit with an empty prompt, confirm the default is used.

### Implementation for User Story 2

- [x] T026 [US2] Add default prompt fallback in `CallService.InitiateCall()` in `ContactCenterPOC-API/Services/CallService.cs`: if the prompt is null or whitespace, read `AzureOpenAI:SystemPrompt` from configuration and use it as the prompt. This implements FR-008 edge case.
- [x] T027 [US2] Enhance prompt scenario UI in `CallCenterPOC-App/Pages/Index.cshtml`: highlight the currently selected scenario (add `active` CSS class on click), ensure clicking a scenario populates the textarea (already works), and confirm the prompt is editable after selection. Move the prompt scenarios list above the textarea for better UX flow.

**Checkpoint**: At this point, User Stories 1, 2, and 3 are all functional. Pre-defined prompts and custom prompts both work correctly.

---

## Phase 6: User Story 4 — Call Recording (Priority: P3)

**Goal**: Call recordings are automatically started on connect and stopped on disconnect, with thread-safe tracking.

**Independent Test**: Place a call, hang up, verify that a recording file was stored in the configured Azure Blob Storage container.

### Implementation for User Story 4

- [x] T028 [US4] Move recording ID tracking from `_activeRecordings` dictionary to `ActiveCall.RecordingId` property in `ContactCenterPOC-API/Services/CallService.cs`. Update `startRecordingAsync()` to set `activeCall.RecordingId` and `stopRecordingAsync()` to read from `activeCall.RecordingId`. Update `CleanupCall()` to use the `ActiveCall` object. This implements FR-009 and FR-010.
- [x] T029 [US4] Ensure recording stops on all termination paths in `ContactCenterPOC-API/Services/CallService.cs`: operator hang-up (via hang-up endpoint), 5-minute timeout (via CTS callback), call disconnection (via callback event), and error conditions. Add null-check guard for `RecordingId` (recording may not have started if call was never answered). Dispose `ActiveCall.CancellationTokenSource` in the cleanup path to prevent CTS register accumulation. This implements FR-011.

**Checkpoint**: All 4 user stories are now independently functional.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Code quality, observability, and cleanup

- [x] T030 [P] Add structured logging with `callConnectionId` correlation to all call lifecycle log messages in `ContactCenterPOC-API/Services/CallService.cs` and `ContactCenterPOC-API/Controllers/CallbackController.cs`. Use `_logger.LogInformation("Call {CallConnectionId} ...")` pattern. Phone numbers are logged unmasked (justified for POC — see plan.md Complexity Tracking). This implements FR-013.
- [x] T031 [P] Remove unused `StartCallInteractionToPlaySound()` and `HandlePlaybackCompleted()` methods from `ContactCenterPOC-API/Services/CallService.cs`, and corresponding `HandlePlaybackCompleted` in `CallbackController.cs`. These use the blocking `WaitForEventProcessorAsync()` pattern identified as problematic in research.md §2.
- [x] T032 [P] Remove unused `HandleCallConnected` method from `ContactCenterPOC-API/Controllers/CallbackController.cs` (dead code — never called).
- [x] T033 [P] Update `TestController` in `ContactCenterPOC-API/Controllers/TestController.cs`: either apply the same E.164 phone validation (FR-002) and concurrent call limit check (FR-017) as `CallController.InitiateCall`, or add a `#if DEBUG` / `[ApiExplorerSettings(IgnoreApi = true)]` guard so it cannot be called in non-development environments. The current implementation bypasses all validation.
- [x] T034 [P] [US3] Add graceful call termination on AI failure in `ContactCenterPOC-API/Services/AzureOpenAIService.cs`: on `ConversationErrorUpdate` and in the catch blocks of `GetOpenAiStreamResponseAsync()`, call back to `CallService.HangUpCall(callConnectionId)` to terminate the ACS call instead of leaving a silent open line. Pass a `Func<string, Task>` or `Action<string>` hang-up callback into `AzureOpenAIService` constructor to avoid circular dependency. This addresses spec edge case "AI service unavailable or error mid-call."
- [x] T035 [P] [US3] Add graceful call termination on WebSocket drop in `ContactCenterPOC-API/Models/acsMediaStreamingHandler.cs`: in the `finally` block of `ProcessWebSocketAsync()`, call back to `CallService.HangUpCall(callConnectionId)` to terminate the ACS call and clean up resources (stop recording, close AI session). Pass a hang-up callback into the handler constructor. This addresses spec edge case "WebSocket connection drops during a call."
- [ ] T036 Run quickstart.md validation end-to-end: start tunnel, start API, start app, initiate a test call, verify all acceptance scenarios from spec.md US1. Test each of the 4 pre-defined prompt scenarios with a real call and document observed AI behavior per scenario (SC-006). Document any issues found.

---

## Phase 8: Campaign Management (US2 Replacement)

**Purpose**: Replace static `PromptScenario` with persistent `Campaign` entity. Add CRUD API, Blob Storage persistence, and updated frontend campaign selector with creation form.

**Independent Test**: View pre-defined campaigns, create a custom campaign, refresh page (verify persistence), initiate a call with a custom campaign and confirm AI behavior.

### Models & Persistence

- [x] T037 [US2] Define `Campaign` model class in `ContactCenterPOC-API/Models/Campaign.cs` with fields: `Id` (string, GUID), `Title` (string, required), `Description` (string, required), `AiBehaviorInstructions` (string, required), `IsDefault` (bool), `CreatedAt` (DateTimeOffset). Add validation attributes. Per data-model.md §3.
- [x] T038 [P] [US2] Define `CreateCampaignRequest` DTO in `ContactCenterPOC-API/Models/Campaign.cs` with fields: `Title`, `Description`, `AiBehaviorInstructions` — all required with validation attributes. Per contracts/api.yaml `CreateCampaignRequest` schema.

### Tests (Write First)

- [x] T039 [P] [US2] Write `CampaignServiceTests` in `CallCenterPOC-API.Tests/Unit/CampaignServiceTests.cs`: test (a) default campaigns are loaded on startup (4 pre-defined), (b) creating a campaign with a duplicate title is rejected, (c) created campaigns appear in the list, (d) campaigns survive service recreation (simulate persistence). Mock `BlobServiceClient`.
- [x] T040 [P] [US2] Write `CampaignControllerContractTests` in `CallCenterPOC-API.Tests/Contract/CampaignControllerContractTests.cs`: test (a) `GET /api/Campaign` returns 200 with array of campaigns, (b) `POST /api/Campaign` with valid body returns 201, (c) `POST /api/Campaign` with missing title returns 400, (d) `POST /api/Campaign` with duplicate title returns 400.

### Service & Controller

- [x] T041 [US2] Implement `CampaignService` in `ContactCenterPOC-API/Services/CampaignService.cs`: load campaigns from Blob Storage (`campaigns.json`) on startup with 4 pre-defined defaults if file doesn't exist. Methods: `GetAllAsync()`, `GetByIdAsync(string id)`, `CreateAsync(CreateCampaignRequest)`. Write-through to Blob Storage on create. Validate title uniqueness. Use `Azure.Storage.Blobs.BlobServiceClient` injected via DI. Per data-model.md §3 and plan.md persistence strategy.
- [x] T042 [US2] Implement `CampaignController` in `ContactCenterPOC-API/Controllers/CampaignController.cs`: `GET /api/Campaign` returns all campaigns. `POST /api/Campaign` creates a new campaign (validates, returns 201 with created campaign or 400). Per contracts/api.yaml.
- [x] T043 [US2] Add `Azure.Storage.Blobs` NuGet package to `ContactCenterPOC-API/ContactCenterPOC-API.csproj`. Register `BlobServiceClient` in `ContactCenterPOC-API/Program.cs` DI using `DefaultAzureCredential` + storage account URI (consistent with existing Managed Identity auth pattern for Azure deployment). For local development, support a `BlobStorage:ConnectionString` config key as a fallback (if set, use connection string; otherwise, use `DefaultAzureCredential` with `BlobStorage:AccountUri`). Register `CampaignService` as singleton. Document both auth options in T078's quickstart update.

### Frontend Updates

- [x] T044 [US2] Update `CallCenterPOC-App/Pages/Index.cshtml`: replace the static `PromptScenario` card list with a dynamic campaign selector. Load campaigns from `GET /api/Campaign` via JavaScript on page load. Display campaigns as selectable cards showing title and description. When selected, populate a hidden `campaignId` field. Keep the prompt textarea for overrides.
- [x] T045 [US2] Add "Create Campaign" form to `CallCenterPOC-App/Pages/Index.cshtml`: a collapsible section with fields for Title, Description, and AI Behavior Instructions. On submit, `POST /api/Campaign` and add the new campaign to the selector list. Show validation errors inline.

### Integration

- [x] T046 [US2] Update `CallRequest` DTO in `ContactCenterPOC-API/Models/CallbackEventModels.cs`: add `CampaignId` (string?, optional) field alongside `Prompt`. Update `CallService.InitiateCall()` to resolve the prompt: if `Prompt` is provided, use it directly; else if `CampaignId` is provided, look up the campaign and use its `AiBehaviorInstructions`; else use the default system prompt.
- [x] T047 [US2] Update `ActiveCall` in `ContactCenterPOC-API/Models/ActiveCall.cs`: add `CampaignId` (string?) and `CampaignTitle` (string?) fields. Populate them during `CallService.InitiateCall()` when a campaign is selected. These are snapshotted for call history persistence.

**Checkpoint**: Campaign CRUD works, campaigns persist, operator can create and select campaigns for calls.

---

## Phase 9: Real-Time Sentiment Analysis (US5)

**Purpose**: Analyze each transcript segment for sentiment in real time using Azure OpenAI chat completion. Stream sentiment alongside transcript entries via SignalR.

**Independent Test**: Place a call, speak with varying sentiment ("I'm very happy" vs "this is terrible"), verify sentiment badges appear next to each transcript entry.

### Models

- [x] T048 [US5] Define `SentimentResult` model in `ContactCenterPOC-API/Models/SentimentResult.cs`: class with `Label` (SentimentLabel enum: Positive, Neutral, Negative) and `Confidence` (float). Add `SentimentLabel` enum in the same file. Per data-model.md §5.
- [x] T049 [US5] Update `TranscriptEntry` in `ContactCenterPOC-API/Models/TranscriptEntry.cs`: add `Sentiment` (SentimentResult) property. Default to `Neutral` with confidence 0 if sentiment analysis hasn't completed yet.

### Tests (Write First)

- [x] T050 [P] [US5] Write `SentimentAnalysisTests` in `CallCenterPOC-API.Tests/Unit/SentimentAnalysisTests.cs`: test (a) positive text returns Positive label, (b) negative text returns Negative label, (c) neutral text returns Neutral label, (d) empty/null text returns Neutral, (e) service handles API errors gracefully by returning Neutral. Mock `ChatClient`.

### Service

- [x] T051 [US5] Implement `SentimentAnalysisService` in `ContactCenterPOC-API/Services/SentimentAnalysisService.cs`: inject Azure OpenAI `ChatClient` using the same auth pattern as `AzureOpenAIService` (API key for local dev via `AzureOpenAI:Key`, `DefaultAzureCredential` for Azure deployment). Method: `Task<SentimentResult> AnalyzeAsync(string text)` — sends a chat completion request with a system prompt instructing the model to classify sentiment as Positive/Neutral/Negative and return a JSON object `{"label":"...","confidence":0.X}`. Parse the response. On error, return `Neutral` with confidence 0. Use a lightweight model (gpt-4o-mini or similar). Per spec.md assumptions.
- [x] T052 [US5] Register `SentimentAnalysisService` as singleton in `ContactCenterPOC-API/Program.cs`. Configure the chat model deployment name via `AzureOpenAI:ChatDeployment` config key (may differ from the Realtime model).

### Integration

- [x] T053 [US5] Update `AzureOpenAIService.GetOpenAiStreamResponseAsync()` in `ContactCenterPOC-API/Services/AzureOpenAIService.cs`: after creating a `TranscriptEntry` (for both AI and Recipient transcripts), call `SentimentAnalysisService.AnalyzeAsync(entry.Text)` and attach the result to `entry.Sentiment` before sending via SignalR. Use `Task.Run()` or fire-and-forget to avoid blocking the audio pipeline — send the transcript immediately, then update sentiment via a follow-up SignalR event if needed.
- [x] T054 [US5] Add a new SignalR server-to-client event `SentimentUpdate` in the TranscriptHub: sends `{ callConnectionId, entryTimestamp, sentiment }` so the frontend can update an already-rendered transcript entry with its sentiment badge asynchronously (avoids blocking transcript delivery on sentiment analysis).

### Frontend

- [x] T055 [US5] Update `CallCenterPOC-App/wwwroot/js/site.js`: handle the `SentimentUpdate` SignalR event. When received, find the matching transcript entry by timestamp and add a colored badge: green for Positive, gray for Neutral, red for Negative. Initially render transcript entries without sentiment (or with a loading dot), then update when sentiment arrives.
- [x] T056 [US5] Update `CallCenterPOC-App/wwwroot/css/site.css`: add styles for sentiment badges (`.sentiment-positive`, `.sentiment-neutral`, `.sentiment-negative`) with appropriate colors and a subtle animation on update.

**Checkpoint**: Sentiment badges appear on transcript entries in real time during calls.

---

## Phase 10: Multi-Number Calling (US6)

**Purpose**: Support initiating up to 2 simultaneous calls in a single action. Each call is independent with its own AI session, transcript, and call panel.

**Independent Test**: Enter 2 phone numbers, click "Make a Call," answer both phones, verify each has an independent AI conversation with its own transcript and sentiment. Hang up one — the other continues.

### API Changes

- [x] T057 [US6] Update `CallRequest` DTO in `ContactCenterPOC-API/Models/CallbackEventModels.cs`: replace `PhoneNumber` (string) with `PhoneNumbers` (string[], required, 1–2 items). Add `[MinLength(1)]` and `[MaxLength(2)]` validation. Add E.164 regex validation per item. Per data-model.md §1 and contracts/api.yaml.
- [x] T058 [US6] Update `CallService.InitiateCall()` in `ContactCenterPOC-API/Services/CallService.cs`: accept `string[] phoneNumbers` instead of `string phoneNumber`. Loop through the array and call `CreateCallAsync()` for each number. Each call gets its own `ActiveCall` entry, its own `CancellationTokenSource`, and its own callback URI (with distinct `targetNumber` query param for WebSocket correlation). Check that `_activeCalls.Count + phoneNumbers.Length <= 5` before starting any calls. Return a list of `{callConnectionId, phoneNumber}` tuples.
- [x] T059 [US6] Update `POST /api/Call/initiate` in `ContactCenterPOC-API/Controllers/CallController.cs`: update to use `CallRequest.PhoneNumbers`. Return `CallInitiatedResponse` with a `calls` array of `{callConnectionId, phoneNumber}` objects. Update 429 response to indicate how many slots are available. Per contracts/api.yaml.

### Tests

- [x] T060 [P] [US6] Update `PhoneNumberValidationTests` in `CallCenterPOC-API.Tests/Unit/PhoneNumberValidationTests.cs`: add tests for (a) array of 1 valid number → pass, (b) array of 2 valid numbers → pass, (c) array of 3 numbers → fail (max 2), (d) empty array → fail, (e) array with 1 valid + 1 invalid → fail.
- [x] T061 [P] [US6] Update `ConcurrentCallLimitTests` in `CallCenterPOC-API.Tests/Unit/ConcurrentCallLimitTests.cs`: add tests for (a) 4 active + 2 new → reject (would exceed 5), (b) 3 active + 2 new → accept (total 5).

### Frontend

- [x] T062 [US6] Update `CallCenterPOC-App/Pages/Index.cshtml`: replace single phone number input with two phone number input fields. The second field is optional (labeled "Phone Number 2 (optional)"). Both fields have E.164 validation. On form submit, collect non-empty phone numbers into a `phoneNumbers` array.
- [x] T063 [US6] Update `CallCenterPOC-App/wwwroot/js/site.js`: when `CallInitiatedResponse.calls` contains multiple entries, create a separate call panel for each call (side-by-side layout). Each panel has its own status indicator, transcript area, sentiment badges, and hang-up button. Join SignalR groups for all call connection IDs. Handle `TranscriptUpdate`, `SentimentUpdate`, and `CallStatusChanged` events routed to the correct panel by `callConnectionId`.
- [x] T064 [US6] Update `CallCenterPOC-App/wwwroot/css/site.css`: add responsive side-by-side layout for 2 call panels (`.call-panels-container` with flexbox). On narrow screens, stack vertically. Each panel has a border, header with phone number, and scrollable transcript area.

**Checkpoint**: Operator can make 1 or 2 simultaneous calls with independent panels and transcripts.

---

## Phase 11: Call History & Analytics (US7)

**Purpose**: Persist call records on disconnect and provide a Call History page with full transcript, sentiment analytics, and speaker timeline.

**Independent Test**: Make a few calls, navigate to Call History page, verify all calls appear. Click a call to see transcript with sentiment, speaker timeline, and aggregate analytics. Refresh the page — data persists.

### Models & Persistence

- [x] T065 [US7] Define `CallRecord` model in `ContactCenterPOC-API/Models/CallRecord.cs` with fields: `CallConnectionId`, `PhoneNumber`, `CampaignId?`, `CampaignTitle?`, `Prompt`, `Duration` (TimeSpan), `OverallSentiment` (SentimentLabel), `SentimentBreakdown` (object with PositivePercent/NeutralPercent/NegativePercent), `TalkTimeRatio` (object with AiPercent/RecipientPercent), `TranscriptEntries` (List\<TranscriptEntry\>), `StartedAt`, `EndedAt`. Per data-model.md §7.

### Tests (Write First)

- [x] T066 [P] [US7] Write `CallHistoryContractTests` in `CallCenterPOC-API.Tests/Contract/CallHistoryContractTests.cs`: test (a) `GET /api/CallHistory` returns 200 with array, (b) `GET /api/CallHistory/{id}` with valid ID returns 200 with CallRecord, (c) `GET /api/CallHistory/{id}` with invalid ID returns 404.

### Service

- [x] T067 [US7] Implement `CallHistoryService` in `ContactCenterPOC-API/Services/CallHistoryService.cs`: inject `BlobServiceClient`. Methods: `SaveCallRecordAsync(CallRecord)` — serializes to JSON and writes to `call-history/{callConnectionId}.json` in Blob Storage. `GetAllAsync()` — lists all blobs in the `call-history/` prefix, reads each, deserializes, returns sorted by `StartedAt` descending. `GetByIdAsync(string callConnectionId)` — reads a single blob. Cache the list in memory for fast reads, invalidate on save.
- [x] T068 [US7] Register `CallHistoryService` as singleton in `ContactCenterPOC-API/Program.cs`.

### Controller

- [x] T069 [US7] Implement `CallHistoryController` in `ContactCenterPOC-API/Controllers/CallHistoryController.cs`: `GET /api/CallHistory` returns list of `CallHistorySummary` (subset of CallRecord fields). `GET /api/CallHistory/{callConnectionId}` returns full `CallRecord` with transcript and analytics. Per contracts/api.yaml.

### Integration — Persist on Disconnect

- [x] T070 [US7] Update `ActiveCall` in `ContactCenterPOC-API/Models/ActiveCall.cs`: add `TranscriptEntries` (List\<TranscriptEntry\>) property. In `AzureOpenAIService`, after creating each `TranscriptEntry`, add it to `activeCall.TranscriptEntries` so the transcript accumulates for persistence on disconnect.
- [x] T071 [US7] Update call cleanup in `CallService` or `CallbackController`: on `CallDisconnected`, compute `Duration`, `OverallSentiment` (majority label), `SentimentBreakdown`, and `TalkTimeRatio` from `activeCall.TranscriptEntries`. Create a `CallRecord` and call `CallHistoryService.SaveCallRecordAsync()`. Do this before removing the `ActiveCall` from the dictionary.

### Frontend — Call History Page

- [x] T072 [US7] Create `CallCenterPOC-App/Pages/CallHistory.cshtml` and `CallHistory.cshtml.cs`: Razor page that loads call history from `GET /api/CallHistory` on page load. Displays a table/list of calls with columns: Phone Number, Campaign, Duration, Sentiment (colored badge), Date/Time. Each row is clickable to expand.
- [x] T073 [US7] Add call detail expansion in `CallHistory.cshtml`: when a row is clicked, fetch `GET /api/CallHistory/{id}` and render: (a) full transcript with per-entry sentiment badges and speaker labels, (b) sentiment breakdown pie/bar chart (using simple HTML/CSS bars — no charting library needed for POC), (c) talk-time ratio bar (AI vs Recipient), (d) speaker timeline — a horizontal bar divided into colored segments showing who spoke when during the call.
- [x] T074 [US7] Update `CallCenterPOC-App/Pages/Shared/_Layout.cshtml`: add "Call History" link to the navigation bar alongside "Home" and "Privacy."
- [x] T075 [US7] Add styles for call history page in `CallCenterPOC-App/wwwroot/css/site.css`: table/list styling, expandable row animation, sentiment breakdown bars, speaker timeline segments, responsive layout.

**Checkpoint**: Call history persists and displays with full analytics. All Phase 2 features are complete.

---

## Phase 12: Integration Testing & Validation

**Purpose**: End-to-end validation of all Phase 2 features

- [ ] T076 Full integration test: create a custom campaign, initiate a 2-number call using the campaign, verify both calls have independent AI conversations with real-time sentiment badges, hang up one call (other continues), verify both calls appear in Call History with correct transcripts and analytics.
- [ ] T077 Validate persistence: restart the API, verify campaigns and call history survive restart (loaded from Blob Storage).
- [x] T078 Update quickstart.md with new configuration keys: `BlobStorage:ConnectionString`, `AzureOpenAI:ChatDeployment` (for sentiment), and any new frontend config. Document the Call History page and campaign creation flow.

---

## Phase 13: Operations Center Dashboard UX (US8)

**Purpose**: Redesign the frontend as a professional three-panel call operations center dashboard. Left panel for call controls + campaigns. Center panel for live transcript + sentiment graph. Right panel for inline call history. Consolidate from multi-page to single-page dashboard.

**Independent Test**: Open the app, verify three-panel layout. Initiate a call, verify center panel shows live transcript with sentiment graph. Verify right panel shows call history. Create a campaign in the left panel. Verify responsive layout on narrow screens.

### Layout & Structure

- [x] T079 [US8] Redesign `CallCenterPOC-App/Pages/Index.cshtml`: replace the current single-column card layout with a three-panel operations center dashboard. The page uses a full-viewport-height flexbox layout (`height: 100vh` minus nav). **Left panel** (~25% width): phone number inputs (1 and 2), campaign list (loaded dynamically), "New Campaign" button/form, and "Make a Call" button. **Center panel** (~50% width): live call operations area — shows an idle state when no call is active; when a call is active, shows call status, live transcript with sentiment badges, and a live sentiment graph. **Right panel** (~25% width): call history list loaded from `/api/CallHistory`, with clickable rows that expand inline to show call detail (transcript, sentiment breakdown, talk-time ratio). Each panel scrolls independently.
- [x] T080 [US8] Simplify `CallCenterPOC-App/Pages/Shared/_Layout.cshtml`: update the navigation bar for the dashboard. Replace multi-page nav links with a single "Operations Center" branding/title. Remove "Call History" and "Privacy" links (history is now in-panel). Keep the footer minimal. Remove `container` wrapper around `@RenderBody()` to allow full-width dashboard layout.
- [x] T081 [P] [US8] Remove `CallCenterPOC-App/Pages/CallHistory.cshtml` and `CallCenterPOC-App/Pages/CallHistory.cshtml.cs` — the call history UI is now embedded in the right panel of the dashboard (Index.cshtml). The call history API endpoints remain unchanged.

### Live Sentiment Graph

- [x] T082 [US8] Implement a live sentiment analysis graph in the center panel of the dashboard. The graph is a rolling horizontal bar/line chart rendered with pure HTML/CSS/JS (no charting library — POC simplicity). Each transcript entry adds a data point: x-axis = entry index or timestamp, y-axis = sentiment value (Positive=1, Neutral=0, Negative=-1). Update the graph on each `TranscriptUpdate` and `SentimentUpdate` SignalR event. Use a `<canvas>` element with simple line drawing, or CSS bars. Show distinct colors: green (positive), gray (neutral), red (negative). The graph auto-scrolls as new entries arrive.

### JavaScript Refactoring

- [x] T083 [US8] Rewrite `CallCenterPOC-App/wwwroot/js/site.js` to support the three-panel dashboard: (a) **Left panel logic**: load campaigns, create campaign form, phone number validation, call initiation form submission. (b) **Center panel logic**: SignalR connection, `TranscriptUpdate`/`SentimentUpdate`/`CallStatusChanged` event handling, live transcript rendering, sentiment graph updates, multi-call panel support (tabbed or split within center panel). (c) **Right panel logic**: load call history on page load, clickable rows that fetch and expand call detail inline, sentiment breakdown bars, talk-time ratio bars, transcript display within detail. Move all call history JS that was in `CallHistory.cshtml` into `site.js`.

### Styling

- [x] T084 [US8] Rewrite `CallCenterPOC-App/wwwroot/css/site.css` for the operations center dashboard: (a) **Three-panel layout**: `.dashboard-container` with flexbox, fixed viewport height, independently scrollable panels. Left panel dark sidebar style, center panel main content area, right panel secondary sidebar. (b) **Panel headers**: each panel has a styled header with title. (c) **Sentiment graph**: `.sentiment-graph` canvas/container styling. (d) **Call history inline**: compact table/list in right panel, expandable detail with animation. (e) **Dark/professional theme**: dark header/sidebars, light center area, professional color palette suitable for operations centers. (f) **Responsive**: on narrow screens (`<992px`), panels stack vertically with a tab bar for switching. (g) **Call controls**: compact form styling for left panel inputs.

### Multi-Call Center Panel

- [x] T085 [US8] Update center panel for multi-call support: when 2 calls are active simultaneously, the center panel shows a tabbed interface (tab per call, labeled with phone number) or a vertical split. Each tab/section has its own transcript area, sentiment badges, sentiment graph, and hang-up button. Tab switches instantly without losing transcript state. When a call disconnects, its tab shows "Disconnected" and can be dismissed.

### Integration & Cleanup

- [x] T086 [US8] Update `CallCenterPOC-App/Pages/Index.cshtml.cs` (page model): remove any page model logic that is no longer needed with the SPA-style dashboard (e.g., server-side form post for call initiation can be replaced with client-side `fetch` to keep the page from reloading). If the current `OnPostAsync` does a server-side redirect, convert it to a client-side API call with JSON response handling so the dashboard stays on the same page.
- [x] T087 [US8] Update quickstart.md: document the new dashboard layout, remove references to the separate Call History page, and add a screenshot description of the three-panel layout.

**Checkpoint**: The application presents as a professional call operations center with all functionality consolidated into a single three-panel dashboard.

---

## Phase 14: Professional UX Redesign & Campaign Expansion (US2 + US8)

**Purpose**: Transform the dashboard into an enterprise-grade operations center inspired by professional banking/financial services dashboards. Deep navy branded theme, rich campaign cards with colored category badges, professional call controls (End Call/Mute/Hold), chat-bubble transcript, call timer, quick response suggestions, KPI summary cards. Expand default campaigns from 4 generic to 6 outbound-focused scenarios with production-quality AI behavior instructions.

**Independent Test**: Open the app, verify deep navy header with "Contact Center Operations" branding and "Operations Dashboard" badge. Verify 6 campaign cards with distinct colored category badges (Collections=amber, Marketing=blue, Survey=green, Reminder=purple, Renewal=teal, Upsell=orange). Search/filter campaigns. Initiate a call, verify call controls bar (End Call red, Mute gray toggle, Hold gray toggle), call timer counting up, chat-bubble transcript with sentiment dots, sentiment graph, and quick responses panel. Verify right panel shows active call details and call history. After call ends, verify KPI cards update. On narrow screen, verify tab-based navigation.

### Campaign Expansion

- [x] T088 [US2] Update default campaigns in `ContactCenterPOC-API/Services/CampaignService.cs`: replace the current 4 generic defaults ("Rude Receptionist," "Customer Service," "Technical Support Agent," "Loan Collector") with 6 outbound-focused campaigns, each with detailed production-quality AI behavior instructions:
  1. **Bank Loan Collection** (Collections): "You are a professional loan collections agent calling about an overdue loan payment. Be firm but empathetic. Reference the outstanding balance, ask about the customer's financial situation, offer flexible repayment plan options (weekly, bi-weekly, monthly installments), negotiate a realistic payment date, and record any payment commitments. If the customer is hostile, remain calm and professional. Always provide a callback number and reference number before ending the call."
  2. **New Product Marketing** (Marketing): "You are an enthusiastic product marketing specialist introducing a new product to potential customers. Open with a personalized greeting, briefly explain why you're calling, and highlight 3 key benefits of the new product. Answer questions about pricing, features, and availability. Gauge the customer's interest level. If interested, offer to schedule a product demo or send a detailed brochure. If not interested, thank them politely and ask if they'd like to be removed from future calls."
  3. **Customer Satisfaction Survey** (Survey): "You are a friendly survey agent conducting a post-service customer satisfaction survey. Thank the customer for their recent interaction with our company. Ask 5 structured questions using a 1-5 rating scale: overall satisfaction, service quality, response time, staff professionalism, and likelihood to recommend. After each rating, ask for brief feedback. Summarize their responses at the end, thank them for their time, and let them know their feedback helps improve our services."
  4. **Appointment Reminder** (Reminder): "You are a helpful appointment reminder agent. Inform the customer of their upcoming appointment including the date, time, and location. Confirm whether they can still attend. If they need to reschedule, offer 2-3 alternative time slots. Provide any preparation instructions (e.g., bring ID, arrive 15 minutes early, fast for 12 hours). Send a verbal confirmation summary of the final appointment details before ending the call."
  5. **Insurance Policy Renewal** (Renewal): "You are a knowledgeable insurance renewal specialist contacting a customer about their expiring policy. Review their current coverage details, explain what happens if the policy lapses, present renewal options including any premium changes, highlight new coverage enhancements available this term, answer questions about deductibles and coverage limits, and help initiate the renewal process. If the customer wants to compare options, offer to schedule a detailed consultation with an underwriter."
  6. **Subscription Renewal & Upsell** (Upsell): "You are a customer success agent following up on an expiring subscription. Start by confirming the customer's satisfaction with the current service. Present the renewal pricing and any loyalty discounts available. Introduce the premium tier features: priority support, advanced analytics, increased limits, and exclusive content. Compare the value proposition of standard vs. premium plans. Process the renewal decision on the call. If the customer needs time to decide, schedule a follow-up call within 48 hours."
- [x] T089 [US2] Update `CampaignServiceTests` in `CallCenterPOC-API.Tests/Unit/CampaignServiceTests.cs`: update the test that checks default campaign count from 4 to 6. Add assertions that verify the 6 specific campaign titles exist ("Bank Loan Collection," "New Product Marketing," "Customer Satisfaction Survey," "Appointment Reminder," "Insurance Policy Renewal," "Subscription Renewal & Upsell"). Verify each campaign has non-empty `AiBehaviorInstructions` with at least 100 characters.

### Professional Header & Theme

- [x] T090 [US8] Redesign `CallCenterPOC-App/Pages/Shared/_Layout.cshtml`: replace the current nav bar with a professional deep navy header bar (#1a1a2e or #0f172a). Left side: application logo placeholder (phone/headset SVG icon) + "Contact Center Operations" title + "AI-Powered Outbound Calling Platform" subtitle. Right side: "Operations Dashboard" badge with a green status dot. Remove all navigation links (single-page dashboard). Remove the `container` wrapper around `@RenderBody()`. Apply system font stack (Inter, Segoe UI, -apple-system).

### Dashboard Layout & Structure

- [x] T091 [US8] Redesign `CallCenterPOC-App/Pages/Index.cshtml`: complete rewrite with the professional three-panel layout per spec.md US8. **Left panel** (~22% width, dark sidebar #1e293b): campaign search/filter bar → rich campaign cards with colored category badges → Create Campaign button/inline form → phone number inputs (1 and 2) with E.164 hints → collapsible Prompt Override textarea → "Start Call" button. **Center panel** (~56% width, light bg #f8fafc): idle state with logo, instruction text, and KPI cards (Calls Today, Avg Duration, Overall Sentiment, Success Rate) → active call state with status bar (status badge + phone + campaign badge + timer), call controls bar (End Call red, Mute toggle, Hold toggle, mic level indicator), chat-bubble transcript area, live sentiment graph `<canvas>`, and collapsible Quick Responses panel. **Right panel** (~22% width, dark sidebar #1e293b): active call details card (top) → KPI dashboard (when idle) → scrollable call history list with expandable detail views.

### Styling

- [x] T092 [US8] Rewrite `CallCenterPOC-App/wwwroot/css/site.css` for the professional operations center:
  - **CSS Variables**: Define design system tokens — `--navy-dark: #0f172a`, `--sidebar-bg: #1e293b`, `--sidebar-card: #334155`, `--center-bg: #f8fafc`, `--text-light: #f1f5f9`, `--text-dark: #1e293b`, `--accent-green: #22c55e`, `--accent-red: #ef4444`, `--accent-amber: #f59e0b`, `--accent-blue: #3b82f6`, `--accent-purple: #8b5cf6`, `--accent-teal: #14b8a6`, `--accent-orange: #f97316`, `--neutral-gray: #94a3b8`
  - **Header**: Deep navy gradient, white text, subtle border bottom
  - **Three-panel layout**: Flexbox with fixed viewport height, independently scrollable panels with custom scrollbar styling
  - **Campaign cards**: Dark card backgrounds (#334155) with left border accent matching campaign category color, bold title, category badge (pill-shaped with category color bg), description text, hover lift effect with shadow
  - **Call controls bar**: Flexbox centered, large circular icon buttons (48px), End Call with red bg, Mute/Hold with gray bg and toggle-active states, mic level animated bar
  - **Chat-bubble transcript**: AI messages left-aligned with bot icon and light blue bg (#dbeafe), recipient messages right-aligned with person icon and white bg, rounded corners (12px), timestamp + sentiment dot
  - **Sentiment graph**: Canvas styling with dark border, labeled axes
  - **KPI cards**: Rounded cards with large numeric value, label text, icon, subtle shadow
  - **Quick responses**: Clickable cards with icon + quoted text, hover highlight
  - **Call history**: Compact cards with campaign badge, sentiment dot, hover expand animation
  - **Responsive** (<992px): Tab-based navigation with `.mobile-tab-bar`, panels hidden/shown via `.active-panel`
  - **Typography**: Inter / Segoe UI / system stack, clear size hierarchy (header 24px, panel titles 16px, body 14px, captions 12px)
  - **Transitions**: 200ms ease on hover/active states, smooth scroll behavior

### JavaScript — Call Controls & Campaign Cards

- [x] T093 [US8] Implement professional call controls in `CallCenterPOC-App/wwwroot/js/site.js`:
  - **Call timer**: Start a `setInterval` timer on call connection that updates MM:SS display in the status bar. Stop on disconnection.
  - **Mute button**: Toggle mute state visually (swap mic/mic-off icon, change button style). Note: actual audio muting requires ACS API support — for POC, toggle the visual state and log a message. Wire up to ACS mute API if available.
  - **Hold button**: Toggle hold state visually (swap pause/play icon, change button style). For POC, toggle visual state. Wire up to ACS hold API if available.
  - **Mic level indicator**: Animate a bar based on transcript activity — pulse when a `TranscriptUpdate` arrives for the recipient (simulates mic activity). Alternatively, use a CSS animation.
  - **End Call**: Wire to existing hang-up API (`POST /api/Call/hangup/{id}`) with confirmation.

- [x] T094 [US8] Implement rich campaign cards in `CallCenterPOC-App/wwwroot/js/site.js`:
  - On page load, fetch campaigns from `GET /api/Campaign` and render as rich cards with: bold title, colored category badge (determine color from campaign title keywords — "Collection"→amber, "Marketing"→blue, "Survey"→green, "Reminder"→purple, "Insurance"/"Renewal"→teal, "Upsell"→orange, default→blue), and truncated description.
  - **Search/filter**: Wire the search input to filter campaign cards in real time (case-insensitive title/description match).
  - **Selection**: Click a card to select it (add highlighted border + accent), deselect others. Store selected campaign ID.
  - **Create Campaign**: Inline form toggle, POST to API, re-render card list with new card.

### JavaScript — Quick Responses & KPIs

- [x] T095 [US8] Implement Quick Responses panel in `CallCenterPOC-App/wwwroot/js/site.js`:
  - Define campaign-specific quick responses as a JavaScript map keyed by campaign title keywords:
    - Collections: "I understand your concern, let me review your account," "We can set up a flexible payment plan," "Your next payment is due by [date]," "I can transfer you to a payment specialist"
    - Marketing: "Would you like to hear about the key benefits?", "I can send you a detailed brochure," "We're offering a limited-time introductory price," "Shall I schedule a product demo?"
    - Survey: "On a scale of 1 to 5, how would you rate...?", "Thank you for that feedback," "Your responses help us improve," "We appreciate your time today"
    - Reminder: "Your appointment is scheduled for [date/time]," "Would you like to reschedule?", "Please remember to bring [required items]," "I'll confirm your updated appointment"
    - Renewal: "Your current policy/subscription expires on [date]," "I'd like to review your coverage options," "We have some new features available this term," "I can process that renewal for you right now"
    - Upsell: "Are you satisfied with your current plan?", "Our premium tier includes [features]," "We have a loyalty discount available," "Would you like to upgrade today?"
  - Render as clickable cards with a speech-bubble icon and quoted text. On click, copy text to clipboard and show a brief "Copied!" toast.

- [x] T096 [US8] Implement KPI summary cards in `CallCenterPOC-App/wwwroot/js/site.js`:
  - Track call metrics in JavaScript state: `callsToday` (count), `totalDuration` (sum of seconds), `sentimentScores` (array of final sentiment values), `successfulCalls` (calls that reached Connected status).
  - On call completion (status → Disconnected), increment counters and recalculate averages.
  - Render KPI cards in center panel idle state: "Calls Today" (number), "Avg Duration" (MM:SS), "Overall Sentiment" (% positive or average score), "Success Rate" (% connected/total).
  - Update cards in real-time via SignalR `CallStatusChanged` events.
  - Persist counts to `sessionStorage` so they survive page navigation within the session.

### JavaScript — Chat-Bubble Transcript

- [x] T097 [US8] Implement chat-bubble style transcript in `CallCenterPOC-App/wwwroot/js/site.js`:
  - Replace the current plain-text transcript rendering with chat-bubble style.
  - **AI messages**: Left-aligned bubble with bot icon (SVG or emoji), light blue background (#dbeafe), rounded corners (12px, square top-left), speaker label "AI", timestamp (HH:MM:SS), sentiment dot (colored circle 8px).
  - **Recipient messages**: Right-aligned bubble with person icon, white/light gray background, rounded corners (12px, square top-right), speaker label "Caller", timestamp, sentiment dot.
  - Auto-scroll to the latest message with smooth scrolling.
  - On `SentimentUpdate` SignalR event, update the sentiment dot color on the matching transcript entry.

### Responsive Layout

- [x] T098 [P] [US8] Implement responsive tab-based navigation in `site.css` and `site.js`: on viewports < 992px, hide the three-panel layout and show a mobile tab bar with three tabs: "Campaigns" (left panel), "Live Call" (center panel), "History" (right panel). Only the active tab's panel is visible. Tab switching animates with a slide transition. The mobile tab bar is fixed at the top below the header.

### Documentation

- [x] T099 [US8] Update quickstart.md: document the professional dashboard design, the 6 default outbound campaigns with their category colors, the call controls (End Call, Mute, Hold), KPI cards, quick responses feature, and chat-bubble transcript. Remove references to the old 4-campaign defaults.

**Checkpoint**: The application presents as a professional, enterprise-grade call operations center with branded deep navy theme, 6 outbound campaigns with colored badges, professional call controls, chat-bubble transcript, KPI summary, and quick response suggestions.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion — **BLOCKS all user stories**
- **US3 (Phase 3)**: Depends on Foundational — must complete before US1 (US1 frontend consumes US3's SignalR events)
- **US1 (Phase 4)**: Depends on US3 — this is the MVP delivery point
- **US2 (Phase 5)**: Depends on Foundational only — can run in parallel with US3/US1 if needed, but sequentially is simpler
- **US4 (Phase 6)**: Depends on Foundational only — can run in parallel with other stories
- **Polish (Phase 7)**: Depends on all user stories being complete
- **Campaign Management (Phase 8)**: Depends on Phase 7 (builds on top of completed Phase 1 codebase). Replaces PromptScenario with Campaign. Requires `Azure.Storage.Blobs` package.
- **Sentiment Analysis (Phase 9)**: Depends on Phase 7 (builds on completed codebase with TranscriptEntry + SignalR pipeline from Phase 3). Does NOT depend on Phase 8 — sentiment operates on TranscriptEntry independent of Campaign. Can run in parallel with Phase 8.
- **Multi-Number Calling (Phase 10)**: Depends on Phase 8 (needs updated CallRequest with CampaignId). Can run in parallel with Phase 9.
- **Call History (Phase 11)**: Depends on Phase 9 (needs SentimentResult on TranscriptEntry) and Phase 10 (needs multi-number support in CallRecord). Last feature phase.
- **Integration Testing (Phase 12)**: Depends on all feature phases (8–11) being complete.
- **Operations Center Dashboard (Phase 13)**: Depends on Phase 11 (needs all features working — campaigns, sentiment, multi-call, call history). Pure frontend rework, no API changes.
- **Professional UX Redesign (Phase 14)**: Depends on Phase 13 (builds on the three-panel dashboard). Primarily frontend + campaign data update. No new API endpoints.

### User Story Dependencies

```
Phase 1–7 (Complete — MVP deployed)
    │
    ▼
Phase 8: Campaign Management (US2 replacement)
    │                                  │
    │                                  │ (Phase 9 is independent of Phase 8)
    │                                  │
    ├──────────────────────────────────┤
    ▼                                  ▼
Phase 10: Multi-Number Calling (US6)  Phase 9: Sentiment Analysis (US5)
    │                                  │
    └──────────────┬───────────────────┘
                   ▼
Phase 11: Call History & Analytics (US7)
                   │
                   ├──────────────────────────┐
                   ▼                          ▼
Phase 12: Integration Testing    Phase 13: Dashboard UX (US8)
                                          │
                                          ▼
                                 Phase 14: Professional UX Redesign
                                          │
                                          ▼
                                 Final Deploy & Validation
```

### Within Each User Story

- Tests (where included) MUST be written and FAIL before implementation
- Models before services
- Services before endpoints/controllers
- Backend before frontend
- Core implementation before integration

### Parallel Opportunities

- **Phase 1**: T001 and T002 can run in parallel
- **Phase 2**: T003, T004, T005 can run in parallel (models); T007 and T009 can run in parallel; T006 must run after T003 (needs ActiveCall model); T008 must run after T007 (needs TranscriptHub)
- **Phase 3**: T010 and T011 can start in parallel (different files); T012 depends on T010+T011; T013 is independent
- **Phase 4**: T014, T015, T016 tests can all run in parallel; T020 and T021 can run in parallel (different endpoints); T023, T024 are sequential (cshtml before js)
- **Phase 7**: T030, T031, T032, T033, T034, T035 can all run in parallel

---

## Parallel Example: User Story 1 (Phase 4)

```bash
# Launch all tests in parallel (write first, verify they fail):
Task T014: PhoneNumberValidationTests in CallCenterPOC-API.Tests/Unit/
Task T015: ConcurrentCallLimitTests in CallCenterPOC-API.Tests/Unit/
Task T016: CallControllerContractTests in CallCenterPOC-API.Tests/Contract/

# Then implement — these two endpoints can be done in parallel:
Task T020: POST /api/Call/hangup/{id} in CallController.cs
Task T021: GET /api/Call/active in CallController.cs

# Sequential tasks (each depends on the previous):
Task T017: Validation attributes on CallRequest → T022: Controller validation handling
Task T018: Concurrent limit in CallService → T019: Timeout in CallService
Task T023: Frontend HTML → T024: Frontend JavaScript → T025: Page model updates
```

---

## Implementation Strategy

### MVP First (Phase 1 → 2 → 3 → 4) ✅ COMPLETE

1. ~~Complete Phase 1: Setup (test project + SignalR client)~~
2. ~~Complete Phase 2: Foundational (models, thread safety, SignalR hub, CORS)~~
3. ~~Complete Phase 3: US3 — Bidirectional voice with transcript streaming~~
4. ~~Complete Phase 4: US1 — Full operator UX with validation, limits, hang-up~~
5. ~~Deploy/demo — MVP deployed and working~~

### Phase 2 Features (Phase 8 → 9/10 → 11 → 12)

1. Complete Phase 8: Campaign Management — Replace prompts with persistent campaigns + CRUD
2. Complete Phase 9 & 10 in parallel: Sentiment Analysis + Multi-Number Calling
3. Complete Phase 11: Call History & Analytics — Persist call records, review page
4. Complete Phase 12: Integration Testing — Full E2E validation
5. **Deploy Phase 2** — All new features live

### Incremental Delivery

1. ~~Setup + Foundational → Foundation ready~~
2. ~~Add US3 → Transcript streaming works → Internal milestone~~
3. ~~Add US1 → Full operator experience → **Deploy/Demo (MVP!)**~~
4. ~~Add US2 → Polished prompt selection → Deploy/Demo~~
5. ~~Add US4 → Recording with thread-safe tracking → Deploy/Demo~~
6. ~~Polish → Production-quality logging, cleanup → Final delivery~~
7. Add Campaign Management → Persistent campaigns with CRUD → Deploy/Demo
8. Add Sentiment Analysis → Real-time sentiment badges → Deploy/Demo
9. Add Multi-Number Calling → 2 simultaneous calls → Deploy/Demo
10. Add Call History → Post-call review with analytics → **Deploy Phase 2**
11. Integration Testing → Full E2E validation → Final delivery
12. Operations Center Dashboard → Professional three-panel UX → **Deploy Phase 3**
13. Professional UX Redesign → Enterprise-grade dashboard with 6 outbound campaigns, deep navy theme, call controls, chat-bubble transcript, KPI cards, quick responses → **Deploy Phase 4**

### Single Developer Strategy (Recommended for POC)

Work sequentially: Phase 1 → 2 → 3 → 4 → 5 → 6 → 7. Within each phase, exploit parallel opportunities where tasks touch different files. Commit after each completed task or logical group.

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks
- [Story] label maps task to specific user story for traceability
- Each user story should be independently completable and testable
- Tests MUST fail before implementing the feature they test
- Commit after each task or logical group
- Stop at any checkpoint to validate the story independently
- T001–T035 are complete (Phase 1 MVP deployed and working)
- T036 pending (E2E validation of MVP)
- T037–T078 are Phase 2 tasks (campaigns, sentiment, multi-number, call history)
- Phase 2 builds on the working MVP — the existing bidirectional voice, transcript, recording, and prompt scenario features are all functional
- Key refactoring: `CallRequest.PhoneNumber` → `CallRequest.PhoneNumbers` (T057) is the most impactful Phase 2 API change
- Key new dependency: `Azure.Storage.Blobs` (T043) for campaign + call history persistence
- T079–T087 are Phase 3 tasks (operations center dashboard UX redesign)
- Phase 3 is frontend-only — no API changes required. All existing API endpoints remain unchanged.
- The `CallHistory.cshtml` page is removed in T081 — its functionality moves into the right panel of the dashboard
- The dashboard uses `<canvas>` or CSS-only charts for the live sentiment graph — no third-party charting library (POC simplicity)
- T088–T099 are Phase 4 tasks (professional UX redesign + campaign expansion)
- Phase 4 replaces the 4 generic default campaigns with 6 outbound-focused campaigns with detailed AI behavior instructions
- Phase 4 is primarily frontend + campaign data — no new API endpoints required
- Campaign category badge colors: Collections=amber, Marketing=blue, Survey=green, Reminder=purple, Renewal=teal, Upsell=orange
- Quick response suggestions are defined in JavaScript (client-side), keyed by campaign title keywords
- KPI metrics are tracked in-session (sessionStorage) — not persisted server-side for POC simplicity
- Call controls (Mute, Hold) are visual toggles in POC — actual ACS mute/hold API integration is stretch goal
- T100–T112 are Phase 15 tasks (recording playback, sentiment fix, contact names)
- Phase 15 adds US9 (recording playback) and US10 (contact names), and fixes US5 sentiment on Azure
- Recording playback uses ACS CallRecording download API — recordings are MP3 files stored in configured Blob Storage
- Contact names are optional — the AI prompt is dynamically prepended with name instructions when provided
- Sentiment fix is a configuration-only change — no code modifications needed

---

## Phase 15: Recording Playback, Sentiment Fix, Contact Names (US4 + US5 + US9 + US10)

**Purpose**: Three new features and one critical bug fix: (1) Allow operators to listen to call recordings from the call history panel, (2) Fix sentiment analysis on Azure (missing `ChatDeployment` config), (3) Add optional contact name fields for personalized AI greetings.

**Independent Test**: Fix sentiment → verify sentiment dots/graph work during live calls. Add contact name → initiate call, verify AI greets by name. Complete a call → click history entry → play recording audio. View history → verify contact name shown.

### Sentiment Fix (US5 Bug)

- [x] T100 [US5] Fix sentiment analysis on Azure: run `az webapp config appsettings set` to add `AzureOpenAI__ChatDeployment=gpt-4o-mini` to the `contactcenterpoc-api` App Service. No code changes required — the `SentimentAnalysisService` and SignalR infrastructure are fully implemented but the ChatDeployment config was missing, causing `_chatClient` to be null and all sentiment to return Neutral.

### Contact Name — Models & API

- [x] T101 [US10] Update `CallRequest` DTO in `ContactCenterPOC-API/Models/CallbackEventModels.cs`: add `ContactNames` (string[]?, optional) field parallel to `PhoneNumbers`. When provided, array indices correspond 1:1 with `PhoneNumbers`.
- [x] T102 [P] [US10] Update `ActiveCall` in `ContactCenterPOC-API/Models/ActiveCall.cs`: add `ContactName` (string?) field to store the contact name for each call.
- [x] T103 [P] [US10] Update `CallRecord` in `ContactCenterPOC-API/Models/CallRecord.cs`: add `ContactName` (string?) field. Update `CallHistorySummary` to include `ContactName`.

### Contact Name — Service Integration

- [x] T104 [US10] Update `CallService.InitiateCall()` in `ContactCenterPOC-API/Services/CallService.cs`: accept `string[]? contactNames` parameter. For each phone number, if a contact name is provided, prepend to the effective prompt: `"IMPORTANT: The person you are calling is named {name}. You MUST greet them by name at the start of the conversation, for example: 'Hello {name}'. "`. Store `ContactName` on the `ActiveCall`.
- [x] T105 [US10] Update `CallController.Initiate()` in `ContactCenterPOC-API/Controllers/CallController.cs`: pass `CallRequest.ContactNames` through to `CallService.InitiateCall()`.
- [x] T106 [US10] Update call disconnect persistence in `CallbackController` or `CallService`: include `ContactName` from `ActiveCall` when creating the `CallRecord`.

### Recording Playback — Models & API

- [x] T107 [US9] Update `CallRecord` in `ContactCenterPOC-API/Models/CallRecord.cs`: add `RecordingId` (string?) field. Update call disconnect persistence to copy `ActiveCall.RecordingId` into the `CallRecord`.
- [x] T108 [US9] Add `GET /api/CallHistory/{callConnectionId}/recording` endpoint in `ContactCenterPOC-API/Controllers/CallHistoryController.cs`: look up the `CallRecord` by ID, retrieve the `RecordingId`, use `CallAutomationClient.GetCallRecording().DownloadStreamingAsync(recordingId)` to download the recording content, and stream it to the client with `Content-Type: audio/mpeg`. Return 404 if no recording exists.

### Recording Playback & Contact Name — Frontend

- [x] T109 [US9] [US10] Update `CallCenterPOC-App/Pages/Index.cshtml`: (a) Add optional contact name `<input>` fields next to each phone number input. (b) Add an `<audio>` element with controls inside the call detail panel (`callDetailPanel`) for recording playback, initially hidden.
- [x] T110 [US9] [US10] Update `CallCenterPOC-App/wwwroot/js/site.js`: (a) Collect contact names from input fields and include in the API call body. (b) In `loadCallDetail()`, if the record has a `recordingId`, show the audio player with `src` set to `{apiBaseUrl}/api/CallHistory/{id}/recording`; otherwise show "No recording available". (c) Display contact name alongside phone number in history list and detail view.
- [x] T111 [US9] [US10] Update `CallCenterPOC-App/wwwroot/css/site.css`: add styles for contact name input fields (compact, alongside phone number) and audio player container (full-width within detail panel, styled border).

### Tests

- [x] T112 [P] [US10] Update `CallControllerContractTests` or add new tests: verify (a) `POST /api/Call/initiate` with `contactNames` array is accepted, (b) `GET /api/CallHistory/{id}` returns `contactName` field, (c) `GET /api/CallHistory/{id}/recording` returns 404 for non-existent recording.

**Checkpoint**: Sentiment works on Azure (dots + graph update in real time). Operators can add contact names for personalized greetings. Historical call recordings are playable from the call detail panel.
