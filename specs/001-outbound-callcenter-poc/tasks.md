# Tasks: Outbound Call Center POC

**Input**: Design documents from `/specs/001-outbound-callcenter-poc/`  
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/api.yaml, quickstart.md

**Tests**: Included Ã¢â‚¬â€ Constitution Principle V requires unit tests for validation logic and contract tests for HTTP endpoints.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3, US4)
- All file paths are relative to the repository root

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create test project and add required client libraries

- [x] T001 Create xUnit test project `CallCenterPOC-API.Tests/CallCenterPOC-API.Tests.csproj` with NuGet packages: `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `Microsoft.AspNetCore.Mvc.Testing`, `Moq`, `coverlet.collector`. Add project reference to `ContactCenter-API/ContactCenter-API.csproj`. Add project to `CallCenterPOC.sln`. Also append `public partial class Program { }` to the bottom of `ContactCenter-API/Program.cs` so `WebApplicationFactory<Program>` can discover the entry point (required for T016 contract tests to compile).
- [x] T002 [P] Add `@microsoft/signalr` JavaScript client library to `ContactCenter-APP/wwwroot/lib/microsoft-signalr/` (via LibMan or CDN download). This is the browser-side SignalR client needed for live transcript streaming.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core data models, thread-safe refactoring, SignalR hub, and CORS Ã¢â‚¬â€ MUST be complete before any user story work begins

**Ã¢Å¡Â Ã¯Â¸Â CRITICAL**: No user story work can begin until this phase is complete

- [x] T003 [P] Define `CallStatus` enum (`Initiating`, `Ringing`, `Connected`, `Disconnected`) and `ActiveCall` class (`CallConnectionId`, `ServerCallId?`, `TargetPhoneNumber`, `Prompt`, `Status`, `StartedAt`, `RecordingId?`, `CancellationTokenSource`) in `ContactCenter-API/Models/ActiveCall.cs` per data-model.md Ã‚Â§2. Note: `ServerCallId` is `string?` Ã¢â‚¬â€ null until the `CallConnected` event populates it (see T013).
- [x] T004 [P] Define `TranscriptEntry` class (`CallConnectionId`, `Speaker`, `Text`, `Timestamp`) and `SpeakerType` enum (`AI`, `Recipient`) in `ContactCenter-API/Models/TranscriptEntry.cs` per data-model.md Ã‚Â§4
- [x] T005 [P] Define `CallStatusUpdate` class (`CallConnectionId`, `Status`, `Message?`, `Timestamp`) in `ContactCenter-API/Models/CallStatusUpdate.cs` per data-model.md Ã‚Â§5
- [x] T006 Refactor `CallService` in `ContactCenter-API/Services/CallService.cs`: replace the four separate `Dictionary` fields (`_activeConnections`, `_activeCallPrompt`, `_activeCallNumbers`, `_activeRecordings`) and the single `_acsMediaStreamingHandler` field with a single `ConcurrentDictionary<string, ActiveCall>` and a `ConcurrentDictionary<string, AcsMediaStreamingHandler>` keyed by `callConnectionId`. Update all methods to use the new dictionaries. This fixes the thread-safety issue identified in research.md Ã‚Â§2.
- [x] T007 [P] Create `TranscriptHub` SignalR hub class in `ContactCenter-API/Hubs/TranscriptHub.cs`. The hub should support client group join/leave by `callConnectionId` (so the frontend subscribes to updates for a specific call). Define methods: `JoinCall(string callConnectionId)`, `LeaveCall(string callConnectionId)`. Server-to-client events: `TranscriptUpdate(TranscriptEntry)`, `CallStatusChanged(CallStatusUpdate)`. Per research.md Ã‚Â§4.
- [x] T008 Register SignalR services, CORS policy, and map the `/transcriptHub` endpoint in `ContactCenter-API/Program.cs`. Add `builder.Services.AddSignalR()`, `builder.Services.AddCors(...)` allowing the origin from the `FrontendOrigin` config key (e.g., `https://localhost:5002`), `app.UseCors(...)`, and `app.MapHub<TranscriptHub>("/transcriptHub")`. The CORS policy must include `.AllowCredentials()` so SignalR can use its WebSocket transport instead of falling back to long-polling.
- [x] T009 [P] Fix WebSocket receive buffer in `ContactCenter-API/Models/acsMediaStreamingHandler.cs`: increase from 2048 bytes to 4096 bytes and implement proper message reassembly by checking `receiveResult.EndOfMessage` before processing. Per research.md Ã‚Â§2.

**Checkpoint**: Foundation ready Ã¢â‚¬â€ thread-safe CallService, data models, SignalR hub, and CORS all in place. User story implementation can now begin.

---

## Phase 3: User Story 3 Ã¢â‚¬â€ Real-Time Bidirectional Voice Conversation (Priority: P1)

**Goal**: Extract live transcript from the AI session and stream it to the operator via SignalR. The existing bidirectional audio bridge and barge-in already work Ã¢â‚¬â€ this phase adds transcript visibility.

**Independent Test**: Place a call, speak to the AI, verify transcript entries appear on the connected SignalR client for both AI and recipient speech, and confirm barge-in still works.

### Implementation for User Story 3

- [x] T010 [US3] Refactor `AcsMediaStreamingHandler` constructor in `ContactCenter-API/Models/acsMediaStreamingHandler.cs` to accept `IHubContext<TranscriptHub>` and the `callConnectionId`. Store both as fields. The hub context will be used to push transcript entries.
- [x] T011 [US3] Refactor `AzureOpenAIService` in `ContactCenter-API/Services/AzureOpenAIService.cs` to accept `IHubContext<TranscriptHub>` and `callConnectionId`. In `GetOpenAiStreamResponseAsync()`: on `ConversationItemStreamingAudioTranscriptionFinishedUpdate`, create a `TranscriptEntry` with `Speaker = SpeakerType.AI` and send it via `hubContext.Clients.Group(callConnectionId).SendAsync("TranscriptUpdate", entry)`. On `ConversationInputTranscriptionFinishedUpdate`, create a `TranscriptEntry` with `Speaker = SpeakerType.Recipient` and send similarly.
- [x] T012 [US3] Update `CallService.StartCallInteraction()` in `ContactCenter-API/Services/CallService.cs` to pass `IHubContext<TranscriptHub>` and `callConnectionId` through to `AcsMediaStreamingHandler` and `AzureOpenAIService`. Inject `IHubContext<TranscriptHub>` into `CallService` constructor.
- [x] T013 [US3] Push `CallStatusUpdate` via SignalR in `CallbackController.CallbackEvent()` in `ContactCenter-API/Controllers/CallbackController.cs`: on `CallConnected` Ã¢â€ â€™ send status `Connected`, and also update the `ActiveCall` entry's `ServerCallId` from `@event.ServerCallId` (needed for recording in T028); on `CallDisconnected` Ã¢â€ â€™ send status `Disconnected`. Inject `IHubContext<TranscriptHub>` into `CallbackController`.

**Checkpoint**: At this point, the AI-powered bidirectional voice conversation works and transcript + status updates are streamed via SignalR. No frontend consumption yet Ã¢â‚¬â€ that comes in US1.

---

## Phase 4: User Story 1 Ã¢â‚¬â€ Initiate an Outbound AI Call (Priority: P1) Ã°Å¸Å½Â¯ MVP

**Goal**: Operator can initiate a validated call, see live status + transcript, hang up, and the system enforces concurrent/duration limits.

**Independent Test**: Enter a valid phone number and prompt, click "Make a Call," answer on a real phone, verify the AI speaks, transcript appears in the UI, click "Hang Up" to terminate. Verify invalid numbers are rejected, 6th concurrent call is rejected, and calls auto-terminate at 5 minutes.

### Tests for User Story 1

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [x] T014 [P] [US1] Write `PhoneNumberValidationTests` in `CallCenterPOC-API.Tests/Unit/PhoneNumberValidationTests.cs`: test E.164 validation Ã¢â‚¬â€ valid numbers (`+6591234567`, `+14155551234`), invalid numbers (`6591234567`, `+0123`, `abc`, empty string, null). Validate against the `[RegularExpression]` attribute on `CallRequest.PhoneNumber`.
- [x] T015 [P] [US1] Write `ConcurrentCallLimitTests` in `CallCenterPOC-API.Tests/Unit/ConcurrentCallLimitTests.cs`: test that `CallService` rejects the 6th concurrent call. Mock `CallAutomationClient`. Add 5 entries to the `ConcurrentDictionary<string, ActiveCall>`, then verify `InitiateCall` throws or returns an error.
- [x] T016 [P] [US1] Write `CallControllerContractTests` in `CallCenterPOC-API.Tests/Contract/CallControllerContractTests.cs`: use `WebApplicationFactory<Program>` to test: (a) `POST /api/Call/initiate` with invalid phone Ã¢â€ â€™ 400, (b) `POST /api/Call/initiate` with missing body Ã¢â€ â€™ 400, (c) `GET /api/Call/active` Ã¢â€ â€™ 200 with expected JSON shape, (d) `POST /api/Call/hangup/nonexistent` Ã¢â€ â€™ 404.

### Implementation for User Story 1

- [x] T017 [US1] Add `[Required]` and `[RegularExpression(@"^\+[1-9]\d{1,14}$")]` validation attributes to `CallRequest.PhoneNumber` in `ContactCenter-API/Models/CallbackEventModels.cs`. Add `[JsonProperty]` for consistent casing. This implements FR-002.
- [x] T018 [US1] Add concurrent call limit check to `CallService.InitiateCall()` in `ContactCenter-API/Services/CallService.cs`: if `_activeCalls.Count >= 5`, throw an `InvalidOperationException` or return a result indicating the limit is reached. This implements FR-017.
- [x] T019 [US1] Add per-call 5-minute timeout in `CallService.InitiateCall()` in `ContactCenter-API/Services/CallService.cs`: create a `CancellationTokenSource`, call `cts.CancelAfter(TimeSpan.FromMinutes(5))`, store it on the `ActiveCall`, and register `cts.Token.Register(async () => { await HangUpCall(callConnectionId); })` to auto-terminate. This implements FR-016. Per research.md Ã‚Â§5.
- [x] T020 [P] [US1] Implement `POST /api/Call/hangup/{callConnectionId}` action in `ContactCenter-API/Controllers/CallController.cs`: look up the active call, call `CallConnection.HangUpAsync(forEveryone: true)`, stop recording, clean up resources, return 200. Return 404 if call not found. Per contracts/api.yaml. This implements FR-015.
- [x] T021 [P] [US1] Implement `GET /api/Call/active` action in `ContactCenter-API/Controllers/CallController.cs`: return `ActiveCallsResponse` with count, maxConcurrent (5), and list of `ActiveCallSummary` objects from the `ConcurrentDictionary`. Per contracts/api.yaml.
- [x] T022 [US1] Update `POST /api/Call/initiate` in `ContactCenter-API/Controllers/CallController.cs`: add `ModelState` validation (return 400 with `ErrorResponse` for invalid input), catch concurrent limit exceeded (return 429 with `ErrorResponse`), and return structured `CallInitiatedResponse` with `callConnectionId` (not `callId`) per contracts/api.yaml. Also update existing code's anonymous response object from `CallId` to `CallConnectionId` for consistency. This implements FR-012.
- [x] T023 [US1] Redesign `ContactCenter-APP/Pages/Index.cshtml`: add a "Call Status" panel below the form showing: status indicator (badge), live transcript area (scrollable div), and a "Hang Up" button. The transcript area displays `TranscriptEntry` items with speaker labels. The hang-up button posts to `/api/Call/hangup/{id}`. Hide the panel when no call is active. This implements FR-014.
- [x] T024 [US1] Implement SignalR client logic in `ContactCenter-APP/wwwroot/js/site.js`: connect to `{ApiBaseUrl}/transcriptHub`, join call group on successful initiation, handle `TranscriptUpdate` (append to transcript div) and `CallStatusChanged` (update status badge, show/hide hang-up button). Handle connection errors gracefully. This implements FR-014.
- [x] T025 [US1] Update `IndexModel.OnPostAsync()` in `ContactCenter-APP/Pages/Index.cshtml.cs` to: (a) capture the `callConnectionId` from the API response and pass it to the page (via `ViewData` or `TempData`) for SignalR group join, (b) handle 429 responses with a user-friendly "concurrent limit reached" message, (c) handle 400 responses with validation error display.

**Checkpoint**: At this point, the operator can initiate a validated call, see live status + transcript, and hang up. Concurrent and duration limits are enforced. User Story 1 is fully functional and independently testable.

---

## Phase 5: User Story 2 Ã¢â‚¬â€ Select a Pre-Defined Prompt Scenario (Priority: P2)

**Goal**: Pre-defined prompt scenarios work correctly with visual selection state, and empty prompts fall back to a default.

**Independent Test**: Click each pre-defined scenario, verify the textarea is populated. Edit the text, initiate a call, confirm the AI uses the modified prompt. Submit with an empty prompt, confirm the default is used.

### Implementation for User Story 2

- [x] T026 [US2] Add default prompt fallback in `CallService.InitiateCall()` in `ContactCenter-API/Services/CallService.cs`: if the prompt is null or whitespace, read `AzureOpenAI:SystemPrompt` from configuration and use it as the prompt. This implements FR-008 edge case.
- [x] T027 [US2] Enhance prompt scenario UI in `ContactCenter-APP/Pages/Index.cshtml`: highlight the currently selected scenario (add `active` CSS class on click), ensure clicking a scenario populates the textarea (already works), and confirm the prompt is editable after selection. Move the prompt scenarios list above the textarea for better UX flow.

**Checkpoint**: At this point, User Stories 1, 2, and 3 are all functional. Pre-defined prompts and custom prompts both work correctly.

---

## Phase 6: User Story 4 Ã¢â‚¬â€ Call Recording (Priority: P3)

**Goal**: Call recordings are automatically started on connect and stopped on disconnect, with thread-safe tracking.

**Independent Test**: Place a call, hang up, verify that a recording file was stored in the configured Azure Blob Storage container.

### Implementation for User Story 4

- [x] T028 [US4] Move recording ID tracking from `_activeRecordings` dictionary to `ActiveCall.RecordingId` property in `ContactCenter-API/Services/CallService.cs`. Update `startRecordingAsync()` to set `activeCall.RecordingId` and `stopRecordingAsync()` to read from `activeCall.RecordingId`. Update `CleanupCall()` to use the `ActiveCall` object. This implements FR-009 and FR-010.
- [x] T029 [US4] Ensure recording stops on all termination paths in `ContactCenter-API/Services/CallService.cs`: operator hang-up (via hang-up endpoint), 5-minute timeout (via CTS callback), call disconnection (via callback event), and error conditions. Add null-check guard for `RecordingId` (recording may not have started if call was never answered). Dispose `ActiveCall.CancellationTokenSource` in the cleanup path to prevent CTS register accumulation. This implements FR-011.

**Checkpoint**: All 4 user stories are now independently functional.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Code quality, observability, and cleanup

- [x] T030 [P] Add structured logging with `callConnectionId` correlation to all call lifecycle log messages in `ContactCenter-API/Services/CallService.cs` and `ContactCenter-API/Controllers/CallbackController.cs`. Use `_logger.LogInformation("Call {CallConnectionId} ...")` pattern. Phone numbers are logged unmasked (justified for POC Ã¢â‚¬â€ see plan.md Complexity Tracking). This implements FR-013.
- [x] T031 [P] Remove unused `StartCallInteractionToPlaySound()` and `HandlePlaybackCompleted()` methods from `ContactCenter-API/Services/CallService.cs`, and corresponding `HandlePlaybackCompleted` in `CallbackController.cs`. These use the blocking `WaitForEventProcessorAsync()` pattern identified as problematic in research.md Ã‚Â§2.
- [x] T032 [P] Remove unused `HandleCallConnected` method from `ContactCenter-API/Controllers/CallbackController.cs` (dead code Ã¢â‚¬â€ never called).
- [x] T033 [P] Update `TestController` in `ContactCenter-API/Controllers/TestController.cs`: either apply the same E.164 phone validation (FR-002) and concurrent call limit check (FR-017) as `CallController.InitiateCall`, or add a `#if DEBUG` / `[ApiExplorerSettings(IgnoreApi = true)]` guard so it cannot be called in non-development environments. The current implementation bypasses all validation.
- [x] T034 [P] [US3] Add graceful call termination on AI failure in `ContactCenter-API/Services/AzureOpenAIService.cs`: on `ConversationErrorUpdate` and in the catch blocks of `GetOpenAiStreamResponseAsync()`, call back to `CallService.HangUpCall(callConnectionId)` to terminate the ACS call instead of leaving a silent open line. Pass a `Func<string, Task>` or `Action<string>` hang-up callback into `AzureOpenAIService` constructor to avoid circular dependency. This addresses spec edge case "AI service unavailable or error mid-call."
- [x] T035 [P] [US3] Add graceful call termination on WebSocket drop in `ContactCenter-API/Models/acsMediaStreamingHandler.cs`: in the `finally` block of `ProcessWebSocketAsync()`, call back to `CallService.HangUpCall(callConnectionId)` to terminate the ACS call and clean up resources (stop recording, close AI session). Pass a hang-up callback into the handler constructor. This addresses spec edge case "WebSocket connection drops during a call."
- [ ] T036 Run quickstart.md validation end-to-end: start tunnel, start API, start app, initiate a test call, verify all acceptance scenarios from spec.md US1. Test each of the 6 pre-defined outbound campaigns with a real call and document observed AI behavior per scenario (SC-006). Document any issues found.

---

## Phase 8: Campaign Management (US2 Replacement)

**Purpose**: Replace static `PromptScenario` with persistent `Campaign` entity. Add CRUD API, Blob Storage persistence, and updated frontend campaign selector with creation form.

**Independent Test**: View pre-defined campaigns, create a custom campaign, refresh page (verify persistence), initiate a call with a custom campaign and confirm AI behavior.

### Models & Persistence

- [x] T037 [US2] Define `Campaign` model class in `ContactCenter-API/Models/Campaign.cs` with fields: `Id` (string, GUID), `Title` (string, required), `Description` (string, required), `AiBehaviorInstructions` (string, required), `IsDefault` (bool), `CreatedAt` (DateTimeOffset). Add validation attributes. Per data-model.md Ã‚Â§3.
- [x] T038 [P] [US2] Define `CreateCampaignRequest` DTO in `ContactCenter-API/Models/Campaign.cs` with fields: `Title`, `Description`, `AiBehaviorInstructions` Ã¢â‚¬â€ all required with validation attributes. Per contracts/api.yaml `CreateCampaignRequest` schema.

### Tests (Write First)

- [x] T039 [P] [US2] Write `CampaignServiceTests` in `CallCenterPOC-API.Tests/Unit/CampaignServiceTests.cs`: test (a) default campaigns are loaded on startup (4 pre-defined), (b) creating a campaign with a duplicate title is rejected, (c) created campaigns appear in the list, (d) campaigns survive service recreation (simulate persistence). Mock `BlobServiceClient`.
- [x] T040 [P] [US2] Write `CampaignControllerContractTests` in `CallCenterPOC-API.Tests/Contract/CampaignControllerContractTests.cs`: test (a) `GET /api/Campaign` returns 200 with array of campaigns, (b) `POST /api/Campaign` with valid body returns 201, (c) `POST /api/Campaign` with missing title returns 400, (d) `POST /api/Campaign` with duplicate title returns 400.

### Service & Controller

- [x] T041 [US2] Implement `CampaignService` in `ContactCenter-API/Services/CampaignService.cs`: load campaigns from Blob Storage (`campaigns.json`) on startup with 4 pre-defined defaults if file doesn't exist. Methods: `GetAllAsync()`, `GetByIdAsync(string id)`, `CreateAsync(CreateCampaignRequest)`. Write-through to Blob Storage on create. Validate title uniqueness. Use `Azure.Storage.Blobs.BlobServiceClient` injected via DI. Per data-model.md Ã‚Â§3 and plan.md persistence strategy.
- [x] T042 [US2] Implement `CampaignController` in `ContactCenter-API/Controllers/CampaignController.cs`: `GET /api/Campaign` returns all campaigns. `POST /api/Campaign` creates a new campaign (validates, returns 201 with created campaign or 400). Per contracts/api.yaml.
- [x] T043 [US2] Add `Azure.Storage.Blobs` NuGet package to `ContactCenter-API/ContactCenter-API.csproj`. Register `BlobServiceClient` in `ContactCenter-API/Program.cs` DI using `DefaultAzureCredential` + storage account URI (consistent with existing Managed Identity auth pattern for Azure deployment). For local development, support a `BlobStorage:ConnectionString` config key as a fallback (if set, use connection string; otherwise, use `DefaultAzureCredential` with `BlobStorage:AccountUri`). Register `CampaignService` as singleton. Document both auth options in T078's quickstart update.

### Frontend Updates

- [x] T044 [US2] Update `ContactCenter-APP/Pages/Index.cshtml`: replace the static `PromptScenario` card list with a dynamic campaign selector. Load campaigns from `GET /api/Campaign` via JavaScript on page load. Display campaigns as selectable cards showing title and description. When selected, populate a hidden `campaignId` field. Keep the prompt textarea for overrides.
- [x] T045 [US2] Add "Create Campaign" form to `ContactCenter-APP/Pages/Index.cshtml`: a collapsible section with fields for Title, Description, and AI Behavior Instructions. On submit, `POST /api/Campaign` and add the new campaign to the selector list. Show validation errors inline.

### Integration

- [x] T046 [US2] Update `CallRequest` DTO in `ContactCenter-API/Models/CallbackEventModels.cs`: add `CampaignId` (string?, optional) field alongside `Prompt`. Update `CallService.InitiateCall()` to resolve the prompt: if `Prompt` is provided, use it directly; else if `CampaignId` is provided, look up the campaign and use its `AiBehaviorInstructions`; else use the default system prompt.
- [x] T047 [US2] Update `ActiveCall` in `ContactCenter-API/Models/ActiveCall.cs`: add `CampaignId` (string?) and `CampaignTitle` (string?) fields. Populate them during `CallService.InitiateCall()` when a campaign is selected. These are snapshotted for call history persistence.

**Checkpoint**: Campaign CRUD works, campaigns persist, operator can create and select campaigns for calls.

---

## Phase 9: Real-Time Sentiment Analysis (US5)

**Purpose**: Analyze each transcript segment for sentiment in real time using Azure OpenAI chat completion. Stream sentiment alongside transcript entries via SignalR.

**Independent Test**: Place a call, speak with varying sentiment ("I'm very happy" vs "this is terrible"), verify sentiment badges appear next to each transcript entry.

### Models

- [x] T048 [US5] Define `SentimentResult` model in `ContactCenter-API/Models/SentimentResult.cs`: class with `Label` (SentimentLabel enum: Positive, Neutral, Negative) and `Confidence` (float). Add `SentimentLabel` enum in the same file. Per data-model.md Ã‚Â§5.
- [x] T049 [US5] Update `TranscriptEntry` in `ContactCenter-API/Models/TranscriptEntry.cs`: add `Sentiment` (SentimentResult) property. Default to `Neutral` with confidence 0 if sentiment analysis hasn't completed yet.

### Tests (Write First)

- [x] T050 [P] [US5] Write `SentimentAnalysisTests` in `CallCenterPOC-API.Tests/Unit/SentimentAnalysisTests.cs`: test (a) positive text returns Positive label, (b) negative text returns Negative label, (c) neutral text returns Neutral label, (d) empty/null text returns Neutral, (e) service handles API errors gracefully by returning Neutral. Mock `ChatClient`.

### Service

- [x] T051 [US5] Implement `SentimentAnalysisService` in `ContactCenter-API/Services/SentimentAnalysisService.cs`: inject Azure OpenAI `ChatClient` using the same auth pattern as `AzureOpenAIService` (API key for local dev via `AzureOpenAI:Key`, `DefaultAzureCredential` for Azure deployment). Method: `Task<SentimentResult> AnalyzeAsync(string text)` Ã¢â‚¬â€ sends a chat completion request with a system prompt instructing the model to classify sentiment as Positive/Neutral/Negative and return a JSON object `{"label":"...","confidence":0.X}`. Parse the response. On error, return `Neutral` with confidence 0. Use a lightweight model (gpt-4o-mini or similar). Per spec.md assumptions.
- [x] T052 [US5] Register `SentimentAnalysisService` as singleton in `ContactCenter-API/Program.cs`. Configure the chat model deployment name via `AzureOpenAI:ChatDeployment` config key (may differ from the Realtime model).

### Integration

- [x] T053 [US5] Update `AzureOpenAIService.GetOpenAiStreamResponseAsync()` in `ContactCenter-API/Services/AzureOpenAIService.cs`: after creating a `TranscriptEntry` (for both AI and Recipient transcripts), call `SentimentAnalysisService.AnalyzeAsync(entry.Text)` and attach the result to `entry.Sentiment` before sending via SignalR. Use `Task.Run()` or fire-and-forget to avoid blocking the audio pipeline Ã¢â‚¬â€ send the transcript immediately, then update sentiment via a follow-up SignalR event if needed.
- [x] T054 [US5] Add a new SignalR server-to-client event `SentimentUpdate` in the TranscriptHub: sends `{ callConnectionId, entryTimestamp, sentiment }` so the frontend can update an already-rendered transcript entry with its sentiment badge asynchronously (avoids blocking transcript delivery on sentiment analysis).

### Frontend

- [x] T055 [US5] Update `ContactCenter-APP/wwwroot/js/site.js`: handle the `SentimentUpdate` SignalR event. When received, find the matching transcript entry by timestamp and add a colored badge: green for Positive, gray for Neutral, red for Negative. Initially render transcript entries without sentiment (or with a loading dot), then update when sentiment arrives.
- [x] T056 [US5] Update `ContactCenter-APP/wwwroot/css/site.css`: add styles for sentiment badges (`.sentiment-positive`, `.sentiment-neutral`, `.sentiment-negative`) with appropriate colors and a subtle animation on update.

**Checkpoint**: Sentiment badges appear on transcript entries in real time during calls.

---

## Phase 10: Multi-Number Calling (US6)

**Purpose**: Support initiating up to 2 simultaneous calls in a single action. Each call is independent with its own AI session, transcript, and call panel.

**Independent Test**: Enter 2 phone numbers, click "Make a Call," answer both phones, verify each has an independent AI conversation with its own transcript and sentiment. Hang up one Ã¢â‚¬â€ the other continues.

### API Changes

- [x] T057 [US6] Update `CallRequest` DTO in `ContactCenter-API/Models/CallbackEventModels.cs`: replace `PhoneNumber` (string) with `PhoneNumbers` (string[], required, 1Ã¢â‚¬â€œ2 items). Add `[MinLength(1)]` and `[MaxLength(2)]` validation. Add E.164 regex validation per item. Per data-model.md Ã‚Â§1 and contracts/api.yaml.
- [x] T058 [US6] Update `CallService.InitiateCall()` in `ContactCenter-API/Services/CallService.cs`: accept `string[] phoneNumbers` instead of `string phoneNumber`. Loop through the array and call `CreateCallAsync()` for each number. Each call gets its own `ActiveCall` entry, its own `CancellationTokenSource`, and its own callback URI (with distinct `targetNumber` query param for WebSocket correlation). Check that `_activeCalls.Count + phoneNumbers.Length <= 5` before starting any calls. Return a list of `{callConnectionId, phoneNumber}` tuples.
- [x] T059 [US6] Update `POST /api/Call/initiate` in `ContactCenter-API/Controllers/CallController.cs`: update to use `CallRequest.PhoneNumbers`. Return `CallInitiatedResponse` with a `calls` array of `{callConnectionId, phoneNumber}` objects. Update 429 response to indicate how many slots are available. Per contracts/api.yaml.

### Tests

- [x] T060 [P] [US6] Update `PhoneNumberValidationTests` in `CallCenterPOC-API.Tests/Unit/PhoneNumberValidationTests.cs`: add tests for (a) array of 1 valid number Ã¢â€ â€™ pass, (b) array of 2 valid numbers Ã¢â€ â€™ pass, (c) array of 3 numbers Ã¢â€ â€™ fail (max 2), (d) empty array Ã¢â€ â€™ fail, (e) array with 1 valid + 1 invalid Ã¢â€ â€™ fail.
- [x] T061 [P] [US6] Update `ConcurrentCallLimitTests` in `CallCenterPOC-API.Tests/Unit/ConcurrentCallLimitTests.cs`: add tests for (a) 4 active + 2 new Ã¢â€ â€™ reject (would exceed 5), (b) 3 active + 2 new Ã¢â€ â€™ accept (total 5).

### Frontend

- [x] T062 [US6] Update `ContactCenter-APP/Pages/Index.cshtml`: replace single phone number input with two phone number input fields. The second field is optional (labeled "Phone Number 2 (optional)"). Both fields have E.164 validation. On form submit, collect non-empty phone numbers into a `phoneNumbers` array.
- [x] T063 [US6] Update `ContactCenter-APP/wwwroot/js/site.js`: when `CallInitiatedResponse.calls` contains multiple entries, create a separate call panel for each call (side-by-side layout). Each panel has its own status indicator, transcript area, sentiment badges, and hang-up button. Join SignalR groups for all call connection IDs. Handle `TranscriptUpdate`, `SentimentUpdate`, and `CallStatusChanged` events routed to the correct panel by `callConnectionId`.
- [x] T064 [US6] Update `ContactCenter-APP/wwwroot/css/site.css`: add responsive side-by-side layout for 2 call panels (`.call-panels-container` with flexbox). On narrow screens, stack vertically. Each panel has a border, header with phone number, and scrollable transcript area.

**Checkpoint**: Operator can make 1 or 2 simultaneous calls with independent panels and transcripts.

---

## Phase 11: Call History & Analytics (US7)

**Purpose**: Persist call records on disconnect and provide a Call History page with full transcript, sentiment analytics, and speaker timeline.

**Independent Test**: Make a few calls, navigate to Call History page, verify all calls appear. Click a call to see transcript with sentiment, speaker timeline, and aggregate analytics. Refresh the page Ã¢â‚¬â€ data persists.

### Models & Persistence

- [x] T065 [US7] Define `CallRecord` model in `ContactCenter-API/Models/CallRecord.cs` with fields: `CallConnectionId`, `PhoneNumber`, `CampaignId?`, `CampaignTitle?`, `Prompt`, `Duration` (TimeSpan), `OverallSentiment` (SentimentLabel), `SentimentBreakdown` (object with PositivePercent/NeutralPercent/NegativePercent), `TalkTimeRatio` (object with AiPercent/RecipientPercent), `TranscriptEntries` (List\<TranscriptEntry\>), `StartedAt`, `EndedAt`. Per data-model.md Ã‚Â§7.

### Tests (Write First)

- [x] T066 [P] [US7] Write `CallHistoryContractTests` in `CallCenterPOC-API.Tests/Contract/CallHistoryContractTests.cs`: test (a) `GET /api/CallHistory` returns 200 with array, (b) `GET /api/CallHistory/{id}` with valid ID returns 200 with CallRecord, (c) `GET /api/CallHistory/{id}` with invalid ID returns 404.

### Service

- [x] T067 [US7] Implement `CallHistoryService` in `ContactCenter-API/Services/CallHistoryService.cs`: inject `BlobServiceClient`. Methods: `SaveCallRecordAsync(CallRecord)` Ã¢â‚¬â€ serializes to JSON and writes to `call-history/{callConnectionId}.json` in Blob Storage. `GetAllAsync()` Ã¢â‚¬â€ lists all blobs in the `call-history/` prefix, reads each, deserializes, returns sorted by `StartedAt` descending. `GetByIdAsync(string callConnectionId)` Ã¢â‚¬â€ reads a single blob. Cache the list in memory for fast reads, invalidate on save.
- [x] T068 [US7] Register `CallHistoryService` as singleton in `ContactCenter-API/Program.cs`.

### Controller

- [x] T069 [US7] Implement `CallHistoryController` in `ContactCenter-API/Controllers/CallHistoryController.cs`: `GET /api/CallHistory` returns list of `CallHistorySummary` (subset of CallRecord fields). `GET /api/CallHistory/{callConnectionId}` returns full `CallRecord` with transcript and analytics. Per contracts/api.yaml.

### Integration Ã¢â‚¬â€ Persist on Disconnect

- [x] T070 [US7] Update `ActiveCall` in `ContactCenter-API/Models/ActiveCall.cs`: add `TranscriptEntries` (List\<TranscriptEntry\>) property. In `AzureOpenAIService`, after creating each `TranscriptEntry`, add it to `activeCall.TranscriptEntries` so the transcript accumulates for persistence on disconnect.
- [x] T071 [US7] Update call cleanup in `CallService` or `CallbackController`: on `CallDisconnected`, compute `Duration`, `OverallSentiment` (majority label), `SentimentBreakdown`, and `TalkTimeRatio` from `activeCall.TranscriptEntries`. Create a `CallRecord` and call `CallHistoryService.SaveCallRecordAsync()`. Do this before removing the `ActiveCall` from the dictionary.

### Frontend Ã¢â‚¬â€ Call History Page

- [x] T072 [US7] Create `ContactCenter-APP/Pages/CallHistory.cshtml` and `CallHistory.cshtml.cs`: Razor page that loads call history from `GET /api/CallHistory` on page load. Displays a table/list of calls with columns: Phone Number, Campaign, Duration, Sentiment (colored badge), Date/Time. Each row is clickable to expand.
- [x] T073 [US7] Add call detail expansion in `CallHistory.cshtml`: when a row is clicked, fetch `GET /api/CallHistory/{id}` and render: (a) full transcript with per-entry sentiment badges and speaker labels, (b) sentiment breakdown pie/bar chart (using simple HTML/CSS bars Ã¢â‚¬â€ no charting library needed for POC), (c) talk-time ratio bar (AI vs Recipient), (d) speaker timeline Ã¢â‚¬â€ a horizontal bar divided into colored segments showing who spoke when during the call.
- [x] T074 [US7] Update `ContactCenter-APP/Pages/Shared/_Layout.cshtml`: add "Call History" link to the navigation bar alongside "Home" and "Privacy."
- [x] T075 [US7] Add styles for call history page in `ContactCenter-APP/wwwroot/css/site.css`: table/list styling, expandable row animation, sentiment breakdown bars, speaker timeline segments, responsive layout.

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

- [x] T079 [US8] Redesign `ContactCenter-APP/Pages/Index.cshtml`: replace the current single-column card layout with a three-panel operations center dashboard. The page uses a full-viewport-height flexbox layout (`height: 100vh` minus nav). **Left panel** (~25% width): phone number inputs (1 and 2), campaign list (loaded dynamically), "New Campaign" button/form, and "Make a Call" button. **Center panel** (~50% width): live call operations area Ã¢â‚¬â€ shows an idle state when no call is active; when a call is active, shows call status, live transcript with sentiment badges, and a live sentiment graph. **Right panel** (~25% width): call history list loaded from `/api/CallHistory`, with clickable rows that expand inline to show call detail (transcript, sentiment breakdown, talk-time ratio). Each panel scrolls independently.
- [x] T080 [US8] Simplify `ContactCenter-APP/Pages/Shared/_Layout.cshtml`: update the navigation bar for the dashboard. Replace multi-page nav links with a single "Operations Center" branding/title. Remove "Call History" and "Privacy" links (history is now in-panel). Keep the footer minimal. Remove `container` wrapper around `@RenderBody()` to allow full-width dashboard layout.
- [x] T081 [P] [US8] Remove `ContactCenter-APP/Pages/CallHistory.cshtml` and `ContactCenter-APP/Pages/CallHistory.cshtml.cs` Ã¢â‚¬â€ the call history UI is now embedded in the right panel of the dashboard (Index.cshtml). The call history API endpoints remain unchanged.

### Live Sentiment Graph

- [x] T082 [US8] Implement a live sentiment analysis graph in the center panel of the dashboard. The graph is a rolling horizontal bar/line chart rendered with pure HTML/CSS/JS (no charting library Ã¢â‚¬â€ POC simplicity). Each transcript entry adds a data point: x-axis = entry index or timestamp, y-axis = sentiment value (Positive=1, Neutral=0, Negative=-1). Update the graph on each `TranscriptUpdate` and `SentimentUpdate` SignalR event. Use a `<canvas>` element with simple line drawing, or CSS bars. Show distinct colors: green (positive), gray (neutral), red (negative). The graph auto-scrolls as new entries arrive.

### JavaScript Refactoring

- [x] T083 [US8] Rewrite `ContactCenter-APP/wwwroot/js/site.js` to support the three-panel dashboard: (a) **Left panel logic**: load campaigns, create campaign form, phone number validation, call initiation form submission. (b) **Center panel logic**: SignalR connection, `TranscriptUpdate`/`SentimentUpdate`/`CallStatusChanged` event handling, live transcript rendering, sentiment graph updates, multi-call panel support (tabbed or split within center panel). (c) **Right panel logic**: load call history on page load, clickable rows that fetch and expand call detail inline, sentiment breakdown bars, talk-time ratio bars, transcript display within detail. Move all call history JS that was in `CallHistory.cshtml` into `site.js`.

### Styling

- [x] T084 [US8] Rewrite `ContactCenter-APP/wwwroot/css/site.css` for the operations center dashboard: (a) **Three-panel layout**: `.dashboard-container` with flexbox, fixed viewport height, independently scrollable panels. Left panel dark sidebar style, center panel main content area, right panel secondary sidebar. (b) **Panel headers**: each panel has a styled header with title. (c) **Sentiment graph**: `.sentiment-graph` canvas/container styling. (d) **Call history inline**: compact table/list in right panel, expandable detail with animation. (e) **Dark/professional theme**: dark header/sidebars, light center area, professional color palette suitable for operations centers. (f) **Responsive**: on narrow screens (`<992px`), panels stack vertically with a tab bar for switching. (g) **Call controls**: compact form styling for left panel inputs.

### Multi-Call Center Panel

- [x] T085 [US8] Update center panel for multi-call support: when 2 calls are active simultaneously, the center panel shows a tabbed interface (tab per call, labeled with phone number) or a vertical split. Each tab/section has its own transcript area, sentiment badges, sentiment graph, and hang-up button. Tab switches instantly without losing transcript state. When a call disconnects, its tab shows "Disconnected" and can be dismissed.

### Integration & Cleanup

- [x] T086 [US8] Update `ContactCenter-APP/Pages/Index.cshtml.cs` (page model): remove any page model logic that is no longer needed with the SPA-style dashboard (e.g., server-side form post for call initiation can be replaced with client-side `fetch` to keep the page from reloading). If the current `OnPostAsync` does a server-side redirect, convert it to a client-side API call with JSON response handling so the dashboard stays on the same page.
- [x] T087 [US8] Update quickstart.md: document the new dashboard layout, remove references to the separate Call History page, and add a screenshot description of the three-panel layout.

**Checkpoint**: The application presents as a professional call operations center with all functionality consolidated into a single three-panel dashboard.

---

## Phase 14: Professional UX Redesign & Campaign Expansion (US2 + US8)

**Purpose**: Transform the dashboard into an enterprise-grade operations center inspired by professional banking/financial services dashboards. Deep navy branded theme, rich campaign cards with colored category badges, professional call controls (End Call/Mute/Hold), chat-bubble transcript, call timer, quick response suggestions, KPI summary cards. Expand default campaigns from 4 generic to 6 outbound-focused scenarios with production-quality AI behavior instructions.

**Independent Test**: Open the app, verify deep navy header with "Contact Center Operations" branding and "Operations Dashboard" badge. Verify 6 campaign cards with distinct colored category badges (Collections=amber, Marketing=blue, Survey=green, Reminder=purple, Renewal=teal, Upsell=orange). Search/filter campaigns. Initiate a call, verify call controls bar (End Call red, Mute gray toggle, Hold gray toggle), call timer counting up, chat-bubble transcript with sentiment dots, sentiment graph, and quick responses panel. Verify right panel shows active call details and call history. After call ends, verify KPI cards update. On narrow screen, verify tab-based navigation.

### Campaign Expansion

- [x] T088 [US2] Update default campaigns in `ContactCenter-API/Services/CampaignService.cs`: replace the current 4 generic defaults ("Rude Receptionist," "Customer Service," "Technical Support Agent," "Loan Collector") with 6 outbound-focused campaigns, each with detailed production-quality AI behavior instructions:
  1. **Bank Loan Collection** (Collections): "You are a professional loan collections agent calling about an overdue loan payment. Be firm but empathetic. Reference the outstanding balance, ask about the customer's financial situation, offer flexible repayment plan options (weekly, bi-weekly, monthly installments), negotiate a realistic payment date, and record any payment commitments. If the customer is hostile, remain calm and professional. Always provide a callback number and reference number before ending the call."
  2. **New Product Marketing** (Marketing): "You are an enthusiastic product marketing specialist introducing a new product to potential customers. Open with a personalized greeting, briefly explain why you're calling, and highlight 3 key benefits of the new product. Answer questions about pricing, features, and availability. Gauge the customer's interest level. If interested, offer to schedule a product demo or send a detailed brochure. If not interested, thank them politely and ask if they'd like to be removed from future calls."
  3. **Customer Satisfaction Survey** (Survey): "You are a friendly survey agent conducting a post-service customer satisfaction survey. Thank the customer for their recent interaction with our company. Ask 5 structured questions using a 1-5 rating scale: overall satisfaction, service quality, response time, staff professionalism, and likelihood to recommend. After each rating, ask for brief feedback. Summarize their responses at the end, thank them for their time, and let them know their feedback helps improve our services."
  4. **Appointment Reminder** (Reminder): "You are a helpful appointment reminder agent. Inform the customer of their upcoming appointment including the date, time, and location. Confirm whether they can still attend. If they need to reschedule, offer 2-3 alternative time slots. Provide any preparation instructions (e.g., bring ID, arrive 15 minutes early, fast for 12 hours). Send a verbal confirmation summary of the final appointment details before ending the call."
  5. **Insurance Policy Renewal** (Renewal): "You are a knowledgeable insurance renewal specialist contacting a customer about their expiring policy. Review their current coverage details, explain what happens if the policy lapses, present renewal options including any premium changes, highlight new coverage enhancements available this term, answer questions about deductibles and coverage limits, and help initiate the renewal process. If the customer wants to compare options, offer to schedule a detailed consultation with an underwriter."
  6. **Subscription Renewal & Upsell** (Upsell): "You are a customer success agent following up on an expiring subscription. Start by confirming the customer's satisfaction with the current service. Present the renewal pricing and any loyalty discounts available. Introduce the premium tier features: priority support, advanced analytics, increased limits, and exclusive content. Compare the value proposition of standard vs. premium plans. Process the renewal decision on the call. If the customer needs time to decide, schedule a follow-up call within 48 hours."
- [x] T089 [US2] Update `CampaignServiceTests` in `CallCenterPOC-API.Tests/Unit/CampaignServiceTests.cs`: update the test that checks default campaign count from 4 to 6. Add assertions that verify the 6 specific campaign titles exist ("Bank Loan Collection," "New Product Marketing," "Customer Satisfaction Survey," "Appointment Reminder," "Insurance Policy Renewal," "Subscription Renewal & Upsell"). Verify each campaign has non-empty `AiBehaviorInstructions` with at least 100 characters.

### Professional Header & Theme

- [x] T090 [US8] Redesign `ContactCenter-APP/Pages/Shared/_Layout.cshtml`: replace the current nav bar with a professional deep navy header bar (#1a1a2e or #0f172a). Left side: application logo placeholder (phone/headset SVG icon) + "Contact Center Operations" title + "AI-Powered Outbound Calling Platform" subtitle. Right side: "Operations Dashboard" badge with a green status dot. Remove all navigation links (single-page dashboard). Remove the `container` wrapper around `@RenderBody()`. Apply system font stack (Inter, Segoe UI, -apple-system).

### Dashboard Layout & Structure

- [x] T091 [US8] Redesign `ContactCenter-APP/Pages/Index.cshtml`: complete rewrite with the professional three-panel layout per spec.md US8. **Left panel** (~22% width, dark sidebar #1e293b): campaign search/filter bar Ã¢â€ â€™ rich campaign cards with colored category badges Ã¢â€ â€™ Create Campaign button/inline form Ã¢â€ â€™ phone number inputs (1 and 2) with E.164 hints Ã¢â€ â€™ collapsible Prompt Override textarea Ã¢â€ â€™ "Start Call" button. **Center panel** (~56% width, light bg #f8fafc): idle state with logo, instruction text, and KPI cards (Calls Today, Avg Duration, Overall Sentiment, Success Rate) Ã¢â€ â€™ active call state with status bar (status badge + phone + campaign badge + timer), call controls bar (End Call red, Mute toggle, Hold toggle, mic level indicator), chat-bubble transcript area, live sentiment graph `<canvas>`, and collapsible Quick Responses panel. **Right panel** (~22% width, dark sidebar #1e293b): active call details card (top) Ã¢â€ â€™ KPI dashboard (when idle) Ã¢â€ â€™ scrollable call history list with expandable detail views.

### Styling

- [x] T092 [US8] Rewrite `ContactCenter-APP/wwwroot/css/site.css` for the professional operations center:
  - **CSS Variables**: Define design system tokens Ã¢â‚¬â€ `--navy-dark: #0f172a`, `--sidebar-bg: #1e293b`, `--sidebar-card: #334155`, `--center-bg: #f8fafc`, `--text-light: #f1f5f9`, `--text-dark: #1e293b`, `--accent-green: #22c55e`, `--accent-red: #ef4444`, `--accent-amber: #f59e0b`, `--accent-blue: #3b82f6`, `--accent-purple: #8b5cf6`, `--accent-teal: #14b8a6`, `--accent-orange: #f97316`, `--neutral-gray: #94a3b8`
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

### JavaScript Ã¢â‚¬â€ Call Controls & Campaign Cards

- [x] T093 [US8] Implement professional call controls in `ContactCenter-APP/wwwroot/js/site.js`:
  - **Call timer**: Start a `setInterval` timer on call connection that updates MM:SS display in the status bar. Stop on disconnection.
  - **Mute button**: Toggle mute state visually (swap mic/mic-off icon, change button style). Note: actual audio muting requires ACS API support Ã¢â‚¬â€ for POC, toggle the visual state and log a message. Wire up to ACS mute API if available.
  - **Hold button**: Toggle hold state visually (swap pause/play icon, change button style). For POC, toggle visual state. Wire up to ACS hold API if available.
  - **Mic level indicator**: Animate a bar based on transcript activity Ã¢â‚¬â€ pulse when a `TranscriptUpdate` arrives for the recipient (simulates mic activity). Alternatively, use a CSS animation.
  - **End Call**: Wire to existing hang-up API (`POST /api/Call/hangup/{id}`) with confirmation.

- [x] T094 [US8] Implement rich campaign cards in `ContactCenter-APP/wwwroot/js/site.js`:
  - On page load, fetch campaigns from `GET /api/Campaign` and render as rich cards with: bold title, colored category badge (determine color from campaign title keywords Ã¢â‚¬â€ "Collection"Ã¢â€ â€™amber, "Marketing"Ã¢â€ â€™blue, "Survey"Ã¢â€ â€™green, "Reminder"Ã¢â€ â€™purple, "Insurance"/"Renewal"Ã¢â€ â€™teal, "Upsell"Ã¢â€ â€™orange, defaultÃ¢â€ â€™blue), and truncated description.
  - **Search/filter**: Wire the search input to filter campaign cards in real time (case-insensitive title/description match).
  - **Selection**: Click a card to select it (add highlighted border + accent), deselect others. Store selected campaign ID.
  - **Create Campaign**: Inline form toggle, POST to API, re-render card list with new card.

### JavaScript Ã¢â‚¬â€ Quick Responses & KPIs

- [x] T095 [US8] Implement Quick Responses panel in `ContactCenter-APP/wwwroot/js/site.js`:
  - Define campaign-specific quick responses as a JavaScript map keyed by campaign title keywords:
    - Collections: "I understand your concern, let me review your account," "We can set up a flexible payment plan," "Your next payment is due by [date]," "I can transfer you to a payment specialist"
    - Marketing: "Would you like to hear about the key benefits?", "I can send you a detailed brochure," "We're offering a limited-time introductory price," "Shall I schedule a product demo?"
    - Survey: "On a scale of 1 to 5, how would you rate...?", "Thank you for that feedback," "Your responses help us improve," "We appreciate your time today"
    - Reminder: "Your appointment is scheduled for [date/time]," "Would you like to reschedule?", "Please remember to bring [required items]," "I'll confirm your updated appointment"
    - Renewal: "Your current policy/subscription expires on [date]," "I'd like to review your coverage options," "We have some new features available this term," "I can process that renewal for you right now"
    - Upsell: "Are you satisfied with your current plan?", "Our premium tier includes [features]," "We have a loyalty discount available," "Would you like to upgrade today?"
  - Render as clickable cards with a speech-bubble icon and quoted text. On click, copy text to clipboard and show a brief "Copied!" toast.

- [x] T096 [US8] Implement KPI summary cards in `ContactCenter-APP/wwwroot/js/site.js`:
  - Track call metrics in JavaScript state: `callsToday` (count), `totalDuration` (sum of seconds), `sentimentScores` (array of final sentiment values), `successfulCalls` (calls that reached Connected status).
  - On call completion (status Ã¢â€ â€™ Disconnected), increment counters and recalculate averages.
  - Render KPI cards in center panel idle state: "Calls Today" (number), "Avg Duration" (MM:SS), "Overall Sentiment" (% positive or average score), "Success Rate" (% connected/total).
  - Update cards in real-time via SignalR `CallStatusChanged` events.
  - Persist counts to `sessionStorage` so they survive page navigation within the session.

### JavaScript Ã¢â‚¬â€ Chat-Bubble Transcript

- [x] T097 [US8] Implement chat-bubble style transcript in `ContactCenter-APP/wwwroot/js/site.js`:
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

- **Setup (Phase 1)**: No dependencies Ã¢â‚¬â€ can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion Ã¢â‚¬â€ **BLOCKS all user stories**
- **US3 (Phase 3)**: Depends on Foundational Ã¢â‚¬â€ must complete before US1 (US1 frontend consumes US3's SignalR events)
- **US1 (Phase 4)**: Depends on US3 Ã¢â‚¬â€ this is the MVP delivery point
- **US2 (Phase 5)**: Depends on Foundational only Ã¢â‚¬â€ can run in parallel with US3/US1 if needed, but sequentially is simpler
- **US4 (Phase 6)**: Depends on Foundational only Ã¢â‚¬â€ can run in parallel with other stories
- **Polish (Phase 7)**: Depends on all user stories being complete
- **Campaign Management (Phase 8)**: Depends on Phase 7 (builds on top of completed Phase 1 codebase). Replaces PromptScenario with Campaign. Requires `Azure.Storage.Blobs` package.
- **Sentiment Analysis (Phase 9)**: Depends on Phase 7 (builds on completed codebase with TranscriptEntry + SignalR pipeline from Phase 3). Does NOT depend on Phase 8 Ã¢â‚¬â€ sentiment operates on TranscriptEntry independent of Campaign. Can run in parallel with Phase 8.
- **Multi-Number Calling (Phase 10)**: Depends on Phase 8 (needs updated CallRequest with CampaignId). Can run in parallel with Phase 9.
- **Call History (Phase 11)**: Depends on Phase 9 (needs SentimentResult on TranscriptEntry) and Phase 10 (needs multi-number support in CallRecord). Last feature phase.
- **Integration Testing (Phase 12)**: Depends on all feature phases (8Ã¢â‚¬â€œ11) being complete.
- **Operations Center Dashboard (Phase 13)**: Depends on Phase 11 (needs all features working Ã¢â‚¬â€ campaigns, sentiment, multi-call, call history). Pure frontend rework, no API changes.
- **Professional UX Redesign (Phase 14)**: Depends on Phase 13 (builds on the three-panel dashboard). Primarily frontend + campaign data update. No new API endpoints.

### User Story Dependencies

```
Phase 1Ã¢â‚¬â€œ7 (Complete Ã¢â‚¬â€ MVP deployed)
    Ã¢â€â€š
    Ã¢â€“Â¼
Phase 8: Campaign Management (US2 replacement)
    Ã¢â€â€š                                  Ã¢â€â€š
    Ã¢â€â€š                                  Ã¢â€â€š (Phase 9 is independent of Phase 8)
    Ã¢â€â€š                                  Ã¢â€â€š
    Ã¢â€Å“Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€Â¤
    Ã¢â€“Â¼                                  Ã¢â€“Â¼
Phase 10: Multi-Number Calling (US6)  Phase 9: Sentiment Analysis (US5)
    Ã¢â€â€š                                  Ã¢â€â€š
    Ã¢â€â€Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€Â¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€Ëœ
                   Ã¢â€“Â¼
Phase 11: Call History & Analytics (US7)
                   Ã¢â€â€š
                   Ã¢â€Å“Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€â‚¬Ã¢â€Â
                   Ã¢â€“Â¼                          Ã¢â€“Â¼
Phase 12: Integration Testing    Phase 13: Dashboard UX (US8)
                                          Ã¢â€â€š
                                          Ã¢â€“Â¼
                                 Phase 14: Professional UX Redesign
                                          Ã¢â€â€š
                                          Ã¢â€“Â¼
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

# Then implement Ã¢â‚¬â€ these two endpoints can be done in parallel:
Task T020: POST /api/Call/hangup/{id} in CallController.cs
Task T021: GET /api/Call/active in CallController.cs

# Sequential tasks (each depends on the previous):
Task T017: Validation attributes on CallRequest Ã¢â€ â€™ T022: Controller validation handling
Task T018: Concurrent limit in CallService Ã¢â€ â€™ T019: Timeout in CallService
Task T023: Frontend HTML Ã¢â€ â€™ T024: Frontend JavaScript Ã¢â€ â€™ T025: Page model updates
```

---

## Implementation Strategy

### MVP First (Phase 1 Ã¢â€ â€™ 2 Ã¢â€ â€™ 3 Ã¢â€ â€™ 4) Ã¢Å“â€¦ COMPLETE

1. ~~Complete Phase 1: Setup (test project + SignalR client)~~
2. ~~Complete Phase 2: Foundational (models, thread safety, SignalR hub, CORS)~~
3. ~~Complete Phase 3: US3 Ã¢â‚¬â€ Bidirectional voice with transcript streaming~~
4. ~~Complete Phase 4: US1 Ã¢â‚¬â€ Full operator UX with validation, limits, hang-up~~
5. ~~Deploy/demo Ã¢â‚¬â€ MVP deployed and working~~

### Phase 2 Features (Phase 8 Ã¢â€ â€™ 9/10 Ã¢â€ â€™ 11 Ã¢â€ â€™ 12)

1. Complete Phase 8: Campaign Management Ã¢â‚¬â€ Replace prompts with persistent campaigns + CRUD
2. Complete Phase 9 & 10 in parallel: Sentiment Analysis + Multi-Number Calling
3. Complete Phase 11: Call History & Analytics Ã¢â‚¬â€ Persist call records, review page
4. Complete Phase 12: Integration Testing Ã¢â‚¬â€ Full E2E validation
5. **Deploy Phase 2** Ã¢â‚¬â€ All new features live

### Incremental Delivery

1. ~~Setup + Foundational Ã¢â€ â€™ Foundation ready~~
2. ~~Add US3 Ã¢â€ â€™ Transcript streaming works Ã¢â€ â€™ Internal milestone~~
3. ~~Add US1 Ã¢â€ â€™ Full operator experience Ã¢â€ â€™ **Deploy/Demo (MVP!)**~~
4. ~~Add US2 Ã¢â€ â€™ Polished prompt selection Ã¢â€ â€™ Deploy/Demo~~
5. ~~Add US4 Ã¢â€ â€™ Recording with thread-safe tracking Ã¢â€ â€™ Deploy/Demo~~
6. ~~Polish Ã¢â€ â€™ Production-quality logging, cleanup Ã¢â€ â€™ Final delivery~~
7. Add Campaign Management Ã¢â€ â€™ Persistent campaigns with CRUD Ã¢â€ â€™ Deploy/Demo
8. Add Sentiment Analysis Ã¢â€ â€™ Real-time sentiment badges Ã¢â€ â€™ Deploy/Demo
9. Add Multi-Number Calling Ã¢â€ â€™ 2 simultaneous calls Ã¢â€ â€™ Deploy/Demo
10. Add Call History Ã¢â€ â€™ Post-call review with analytics Ã¢â€ â€™ **Deploy Phase 2**
11. Integration Testing Ã¢â€ â€™ Full E2E validation Ã¢â€ â€™ Final delivery
12. Operations Center Dashboard Ã¢â€ â€™ Professional three-panel UX Ã¢â€ â€™ **Deploy Phase 3**
13. Professional UX Redesign Ã¢â€ â€™ Enterprise-grade dashboard with 6 outbound campaigns, deep navy theme, call controls, chat-bubble transcript, KPI cards, quick responses Ã¢â€ â€™ **Deploy Phase 4**

### Single Developer Strategy (Recommended for POC)

Work sequentially: Phase 1 Ã¢â€ â€™ 2 Ã¢â€ â€™ 3 Ã¢â€ â€™ 4 Ã¢â€ â€™ 5 Ã¢â€ â€™ 6 Ã¢â€ â€™ 7. Within each phase, exploit parallel opportunities where tasks touch different files. Commit after each completed task or logical group.

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks
- [Story] label maps task to specific user story for traceability
- Each user story should be independently completable and testable
- Tests MUST fail before implementing the feature they test
- Commit after each task or logical group
- Stop at any checkpoint to validate the story independently
- T001Ã¢â‚¬â€œT035 are complete (Phase 1 MVP deployed and working)
- T036 pending (E2E validation of MVP â€” carry-forward quality gate)
- T076 and T077 pending (Phase 12 integration testing â€” carry-forward quality gates, run after all feature phases are deployed)
- T037Ã¢â‚¬â€œT078 are Phase 2 tasks (campaigns, sentiment, multi-number, call history)
- Phase 2 builds on the working MVP Ã¢â‚¬â€ the existing bidirectional voice, transcript, recording, and prompt scenario features are all functional
- Key refactoring: `CallRequest.PhoneNumber` Ã¢â€ â€™ `CallRequest.PhoneNumbers` (T057) is the most impactful Phase 2 API change
- Key new dependency: `Azure.Storage.Blobs` (T043) for campaign + call history persistence
- T079Ã¢â‚¬â€œT087 are Phase 3 tasks (operations center dashboard UX redesign)
- Phase 3 is frontend-only Ã¢â‚¬â€ no API changes required. All existing API endpoints remain unchanged.
- The `CallHistory.cshtml` page is removed in T081 Ã¢â‚¬â€ its functionality moves into the right panel of the dashboard
- The dashboard uses `<canvas>` or CSS-only charts for the live sentiment graph Ã¢â‚¬â€ no third-party charting library (POC simplicity)
- T088Ã¢â‚¬â€œT099 are Phase 4 tasks (professional UX redesign + campaign expansion)
- Phase 4 replaces the 4 generic default campaigns with 6 outbound-focused campaigns with detailed AI behavior instructions
- Phase 4 is primarily frontend + campaign data Ã¢â‚¬â€ no new API endpoints required
- Campaign category badge colors: Collections=amber, Marketing=blue, Survey=green, Reminder=purple, Renewal=teal, Upsell=orange
- Quick response suggestions are defined in JavaScript (client-side), keyed by campaign title keywords
- KPI metrics are tracked in-session (sessionStorage) Ã¢â‚¬â€ not persisted server-side for POC simplicity
- Call controls (Mute, Hold) are visual toggles in POC Ã¢â‚¬â€ actual ACS mute/hold API integration is stretch goal
- T100Ã¢â‚¬â€œT112 are Phase 15 tasks (recording playback, sentiment fix, contact names)
- Phase 15 adds US9 (recording playback) and US10 (contact names), and fixes US5 sentiment on Azure
- Recording playback uses ACS CallRecording download API Ã¢â‚¬â€ recordings are MP3 files stored in configured Blob Storage
- Contact names are optional Ã¢â‚¬â€ the AI prompt is dynamically prepended with name instructions when provided
- Sentiment fix is a configuration-only change Ã¢â‚¬â€ no code modifications needed

---

## Phase 15: Recording Playback, Sentiment Fix, Contact Names (US4 + US5 + US9 + US10)

**Purpose**: Three new features and one critical bug fix: (1) Allow operators to listen to call recordings from the call history panel, (2) Fix sentiment analysis on Azure (missing `ChatDeployment` config), (3) Add optional contact name fields for personalized AI greetings.

**Independent Test**: Fix sentiment Ã¢â€ â€™ verify sentiment dots/graph work during live calls. Add contact name Ã¢â€ â€™ initiate call, verify AI greets by name. Complete a call Ã¢â€ â€™ click history entry Ã¢â€ â€™ play recording audio. View history Ã¢â€ â€™ verify contact name shown.

### Sentiment Fix (US5 Bug)

- [x] T100 [US5] Fix sentiment analysis on Azure: run `az webapp config appsettings set` to add `AzureOpenAI__ChatDeployment=gpt-4o-mini` to the `contactcenterpoc-api` App Service. No code changes required Ã¢â‚¬â€ the `SentimentAnalysisService` and SignalR infrastructure are fully implemented but the ChatDeployment config was missing, causing `_chatClient` to be null and all sentiment to return Neutral.

### Contact Name Ã¢â‚¬â€ Models & API

- [x] T101 [US10] Update `CallRequest` DTO in `ContactCenter-API/Models/CallbackEventModels.cs`: add `ContactNames` (string[]?, optional) field parallel to `PhoneNumbers`. When provided, array indices correspond 1:1 with `PhoneNumbers`.
- [x] T102 [P] [US10] Update `ActiveCall` in `ContactCenter-API/Models/ActiveCall.cs`: add `ContactName` (string?) field to store the contact name for each call.
- [x] T103 [P] [US10] Update `CallRecord` in `ContactCenter-API/Models/CallRecord.cs`: add `ContactName` (string?) field. Update `CallHistorySummary` to include `ContactName`.

### Contact Name Ã¢â‚¬â€ Service Integration

- [x] T104 [US10] Update `CallService.InitiateCall()` in `ContactCenter-API/Services/CallService.cs`: accept `string[]? contactNames` parameter. For each phone number, if a contact name is provided, prepend to the effective prompt: `"IMPORTANT: The person you are calling is named {name}. You MUST greet them by name at the start of the conversation, for example: 'Hello {name}'. "`. Store `ContactName` on the `ActiveCall`.
- [x] T105 [US10] Update `CallController.Initiate()` in `ContactCenter-API/Controllers/CallController.cs`: pass `CallRequest.ContactNames` through to `CallService.InitiateCall()`.
- [x] T106 [US10] Update call disconnect persistence in `CallbackController` or `CallService`: include `ContactName` from `ActiveCall` when creating the `CallRecord`.

### Recording Playback Ã¢â‚¬â€ Models & API

- [x] T107 [US9] Update `CallRecord` in `ContactCenter-API/Models/CallRecord.cs`: add `RecordingId` (string?) field. Update call disconnect persistence to copy `ActiveCall.RecordingId` into the `CallRecord`.
- [x] T108 [US9] Add `GET /api/CallHistory/{callConnectionId}/recording` endpoint in `ContactCenter-API/Controllers/CallHistoryController.cs`: look up the `CallRecord` by ID, retrieve the `RecordingId`, use `CallAutomationClient.GetCallRecording().DownloadStreamingAsync(recordingId)` to download the recording content, and stream it to the client with `Content-Type: audio/mpeg`. Return 404 if no recording exists.

### Recording Playback & Contact Name Ã¢â‚¬â€ Frontend

- [x] T109 [US9] [US10] Update `ContactCenter-APP/Pages/Index.cshtml`: (a) Add optional contact name `<input>` fields next to each phone number input. (b) Add an `<audio>` element with controls inside the call detail panel (`callDetailPanel`) for recording playback, initially hidden.
- [x] T110 [US9] [US10] Update `ContactCenter-APP/wwwroot/js/site.js`: (a) Collect contact names from input fields and include in the API call body. (b) In `loadCallDetail()`, if the record has a `recordingId`, show the audio player with `src` set to `{apiBaseUrl}/api/CallHistory/{id}/recording`; otherwise show "No recording available". (c) Display contact name alongside phone number in history list and detail view.
- [x] T111 [US9] [US10] Update `ContactCenter-APP/wwwroot/css/site.css`: add styles for contact name input fields (compact, alongside phone number) and audio player container (full-width within detail panel, styled border).

### Tests

- [x] T112 [P] [US10] Update `CallControllerContractTests` or add new tests: verify (a) `POST /api/Call/initiate` with `contactNames` array is accepted, (b) `GET /api/CallHistory/{id}` returns `contactName` field, (c) `GET /api/CallHistory/{id}/recording` returns 404 for non-existent recording.

**Checkpoint**: Sentiment works on Azure (dots + graph update in real time). Operators can add contact names for personalized greetings. Historical call recordings are playable from the call detail panel.

---

## Phase 16: History Persistence, Recording Playback Fix, Campaign Prompt UX (Bug Fixes)

**Purpose**: Fix three critical bugs found during production testing: (1) call history not surviving API restarts because Blob Storage was not configured, (2) recording download endpoint broken because it uses ACS `RecordingId` as a URL instead of downloading from Blob Storage, (3) campaign prompt not updating when clicking different campaigns.

**Independent Test**: Deploy API Ã¢â€ â€™ make a call Ã¢â€ â€™ verify history survives page refresh and API restart. Click a completed call in history Ã¢â€ â€™ play recording audio. Select different campaigns Ã¢â€ â€™ verify prompt preview updates and old override text is cleared.

### Notes
- T113Ã¢â‚¬â€œT119 are Phase 16 tasks (history persistence, recording fix, campaign prompt UX)
- Root cause of history loss: `BlobStorage:AccountUri` was never set on Azure, causing `BlobServiceClient` to use `UseDevelopmentStorage=true` fallback which silently fails
- Root cause of recording failure: ACS `RecordingId` is an opaque string, not a download URL Ã¢â‚¬â€ `DownloadStreamingAsync(new Uri(recordingId))` throws
- Root cause of campaign prompt: `selectCampaign()` only updates prompt if textarea is empty, and `initiateCall()` always sends the textarea content as `prompt` which overrides `campaignId` in the API

### Blob Storage Config Fix

- [x] T113 [FR-043] Update `ContactCenter-API/Program.cs`: if neither `BlobStorage:ConnectionString` nor `BlobStorage:AccountUri` is set, derive `BlobStorage:AccountUri` from the `BlobContainer` URL by parsing the storage account base URL (e.g., `https://account.blob.core.windows.net/container` Ã¢â€ â€™ `https://account.blob.core.windows.net`). Also set `BlobStorage__AccountUri` on the Azure App Service.

### Recording Download Fix

- [x] T114 [FR-044] [FR-046] Update `ContactCenter-API/Controllers/CallHistoryController.cs`: replace `DownloadStreamingAsync(new Uri(recordingId))` with direct Blob download. Inject `BlobServiceClient`, read `BlobContainer` config to extract container name, search for blobs matching the recording ID prefix, and stream the first matching MP3 blob. Return 404 with helpful message if no recording blob found.
- [x] T115 [P] [FR-046] Update `CallRecord.cs`: add `HasRecording` (bool) to `CallHistorySummary`. Update `CallHistoryService.ToSummary()`: set `HasRecording = !string.IsNullOrEmpty(record.RecordingId)`.

### Campaign Prompt UX Fix

- [x] T116 [FR-045] Update `ContactCenter-APP/Pages/Index.cshtml`: add a campaign prompt preview section below the campaign cards Ã¢â‚¬â€ a read-only area that shows the selected campaign's AI behavior instructions. Initially hidden, shown when a campaign is selected.
- [x] T117 [FR-045] Update `ContactCenter-APP/wwwroot/js/site.js`: (a) In `selectCampaign()`, always clear the prompt override textarea and show the campaign's `aiBehaviorInstructions` in the preview area. (b) In `initiateCall()`, only include `prompt` in the request body if the prompt override textarea has user-typed text (track via a `promptManuallyEdited` flag). (c) Show a recording indicator icon (Ã°Å¸Å½â„¢) on history list items when `hasRecording` is true.
- [x] T118 [P] Update `ContactCenter-APP/wwwroot/css/site.css`: add styles for the campaign prompt preview area (read-only look, muted background, smaller font, max-height with overflow scroll).

### Azure Configuration

- [x] T119 Set `BlobStorage__AccountUri` on the `contactcenterpoc-api` Azure App Service. Enabled system-assigned Managed Identity, assigned Storage Blob Data Contributor role, fixed `BlobContainer` to be a URL (was incorrectly set to a connection string).

**Checkpoint**: Call history persists across API restarts. Recording playback works from the call detail panel. Campaign prompt preview updates when selecting different campaigns, and prompt override only wins when explicitly typed.

---

## Phase 17 Ã¢â‚¬â€ Sentiment & History Production Fixes

**Purpose**: Fix production issues: (1) call history container name not derived from `BlobContainer` URL Ã¢â‚¬â€ defaults to nonexistent `callcenter-data` container, (2) sentiment analysis returns Neutral due to Azure OpenAI auth/config or model-parameter mismatches, (3) sentiment timeline uses single-message context Ã¢â‚¬â€ enhance with rolling 5-second window for richer (but still reactive) analysis.

**Independent Test**: Deploy API Ã¢â€ â€™ make a call Ã¢â€ â€™ verify call appears in history after page refresh. During a call, verify sentiment dots show Positive/Negative (not all Neutral). Verify sentiment timeline graph moves with the conversation.

### Blob Container Name Fix

- [x] T120 [FR-043] Update `ContactCenter-API/Program.cs`: when deriving `BlobStorage:AccountUri` from `BlobContainer` URL, also extract the container name from the URL path and set `BlobStorage:ContainerName` in configuration (e.g., `https://account.blob.core.windows.net/callsstorage` Ã¢â€ â€™ container name `callsstorage`). This ensures `CallHistoryService` and `CampaignService` use the correct container.

### Sentiment Auth Fix

- [x] T121 [FR-047] Update `ContactCenter-API/Services/SentimentAnalysisService.cs`: authenticate to Azure OpenAI using `DefaultAzureCredential` (Managed Identity / Entra ID) for Azure deployments, with optional API key support for local development when `AzureOpenAI:Key` is configured.
- [x] T122 [FR-047] Assign "Cognitive Services OpenAI User" role to the API app's managed identity (`6ac6f2c6-cdad-4345-bb4a-f286434be5c4`) on the Azure OpenAI resource (`cogsermultiaccess`).

### Rolling Sentiment Window

- [x] T123 [FR-048] Update `ContactCenter-API/Services/AzureOpenAIService.cs`: in `FireAndForgetSentiment()`, instead of analyzing only the current message text, aggregate all transcript entries from the last 5 seconds (from `ActiveCall.TranscriptEntries`) and analyze the concatenated text. This gives richer conversational context while staying responsive.

### Deployment

- [x] T124 Publish and deploy API to Azure. Restart app. Verify campaigns load (confirms container name fix). Verify unit tests pass.

**Checkpoint**: Call history persists to the correct blob container. Sentiment analysis works via managed identity (no more HTTP 403). Sentiment timeline reflects rolling 5-second conversational context.

---

## Phase 18: Transcribe Existing Recordings Into History (US11)

**Purpose**: Operators can transcribe already-stored MP3/WAV recordings from Blob Storage and save the transcript into the historical call record for review.

**Independent Test**: Pick a historical call with a recording. Open the call detail panel and click "Transcribe". Verify the transcript text appears and remains after page refresh (persisted to the call history JSON).

### Models

- [x] T125 [US11] Update `ContactCenter-API/Models/CallRecord.cs`: add `RecordingTranscript` (string?) and `RecordingTranscribedAt` (DateTimeOffset?) fields.

### Backend Services

- [x] T126 [US11] Create `RecordingTranscriptionService` in `ContactCenter-API/Services/RecordingTranscriptionService.cs`: download the recording audio from the configured recording container in Blob Storage and call Azure OpenAI audio transcription (multipart form-data upload).
- [x] T127 [US11] Update `CallHistoryService` in `ContactCenter-API/Services/CallHistoryService.cs`: add `SaveRecordingTranscriptAsync(callConnectionId, transcript)` and ensure the in-memory summary cache is updated idempotently (no duplicate summaries when resaving).

### API

- [x] T128 [US11] Add `POST /api/CallHistory/{callConnectionId}/transcribe?force=false` endpoint in `ContactCenter-API/Controllers/CallHistoryController.cs`:
  - If record not found or no recording exists Ã¢â€ â€™ return 404/400 with a helpful message
  - If transcript already exists and `force=false` Ã¢â€ â€™ return existing record
  - Otherwise Ã¢â€ â€™ transcribe, persist transcript into call history JSON, return updated record

### Frontend

- [x] T129 [US11] Update `ContactCenter-APP/Pages/Index.cshtml`: add a "Recording Transcript" section within the call detail panel (right panel) containing a Transcribe button and a read-only transcript display area.
- [x] T130 [US11] Update `ContactCenter-APP/wwwroot/js/site.js`: wire up the Transcribe button to call the new API endpoint, show progress/errors, and render the persisted transcript in the detail view.

### Documentation

- [x] T131 [US11] Update quickstart.md with required configuration keys for transcription (Azure OpenAI endpoint + transcription deployment name) and document the operator workflow for on-demand transcription.

---

## Phase 19: Call History Durability & Playback Reliability (Production Hardening)

**Purpose**: Ensure historical calls reliably appear across refresh/restart/scale-out, reduce storage thrash, and recover older calls where recordings exist but `call-history/` JSON is missing.

**Independent Test**: Start a call Ã¢â€ â€™ refresh the UI Ã¢â€ â€™ the call appears in history immediately. Complete a call Ã¢â€ â€™ refresh Ã¢â€ â€™ final record still present. If `call-history/` is empty but ACS recordings exist Ã¢â€ â€™ refresh history Ã¢â€ â€™ records are rebuilt and show up. Click recording Ã¢â€ â€™ audio can seek (range requests).

- [x] T132 Persist initial call record at initiation in `ContactCenter-API/Services/CallService.cs`: after `CreateCallAsync()` returns `callConnectionId`, immediately write a minimal `CallRecord` via `CallHistoryService.SaveCallRecordAsync()` (duration=0, transcript empty) so new calls show up in history even if disconnect persistence is delayed.
- [x] T133 Add short-lived call history summary cache with TTL in `ContactCenter-API/Services/CallHistoryService.cs`: maintain `_summaryCache` and refresh at most every N seconds (default 5) using `CallHistory:CacheTtlSeconds` to prevent stale empties and reduce repeated blob listing.
- [x] T134 Auto-rebuild history from ACS recording metadata when `call-history/` is empty in `ContactCenter-API/Services/CallHistoryService.cs`: scan for `*/0-acsmetadata.json`, parse `{date}/{callId}/{recordingId}/0-acsmetadata.json`, extract PSTN phone number (when present), synthesize minimal `CallRecord`, and persist it under `call-history/`.
- [x] T135 Enable HTTP range support for recording playback in `ContactCenter-API/Controllers/CallHistoryController.cs`: serve audio using `FileStreamResult` with `EnableRangeProcessing = true` so the browser `<audio>` element can seek.
- [x] T135a Update quickstart.md: document `CallHistory:CacheTtlSeconds` configuration key (default 5 s) and the automatic history rebuild behavior when `call-history/` is empty but ACS recordings exist.

---

## Phase 20: Dashboard Navigation UX (Live vs Historical Tabs)

**Purpose**: Make it obvious which calls are live vs completed, and make the center panel the single primary detail/work area.

**Independent Test**: Right pane shows two tabs: Live calls and Historical calls. Clicking a historical item shows recording+transcript in the center. Clicking/starting a live call switches the center back to live operations.

- [x] T136 Update `ContactCenter-APP/Pages/Index.cshtml`: add Bootstrap nav tabs in the right pane (**Live calls** / **Historical calls**). Move historical call detail markup (recording player, transcript, analytics) into the **center panel** so details render in the middle.
- [x] T137 Update `ContactCenter-APP/wwwroot/js/site.js`: render a live calls list (`updateLiveCallsList()`), add center-panel switching helpers (e.g., `ensureLiveOperationsVisible()`), and ensure these behaviors:
  - Selecting a historical call hides live ops and shows historical detail in center
  - Selecting a live call or initiating a new call returns center to live ops
  - Refreshing history does not blank the center while a historical detail is open

---

## Phase 21: Sentiment Compatibility & Privacy Tweaks

**Purpose**: Address two production-facing concerns: (1) Azure OpenAI deployments like `gpt-5-nano` can reject certain chat-completions parameters, and (2) call history should not display full phone numbers.

**Independent Test**: Place a call and verify sentiment dots are not all Neutral. Open the Historical calls list/details and confirm phone numbers are masked (only last 4 digits visible).

### Sentiment Request Compatibility

- [x] T138 Update `ContactCenter-API/Services/SentimentAnalysisService.cs`: call the Azure OpenAI chat completions REST endpoint with model-compatible parameters (e.g., use `max_completion_tokens` and omit `temperature` for `gpt-5-nano`), and retry with legacy parameters only if the error indicates unsupported parameters.

### Phone Number Masking

- [x] T139 Add `ContactCenter-API/Services/PhoneNumberMasker.cs` and apply masking to:
  - Call history summaries (list view)
  - Call history detail/transcription responses
  Masking is applied at response time (persisted history blobs remain unchanged).

---

## Phase 22: Dual-Speaker Emotion Graphs + Operator Style Traits

**Purpose**: Add per-speaker emotion detection (operator/AI vs customer/recipient), render two live emotion graphs, persist the emotion timeline into call history, and compute operator style traits (empathetic/energetic).

**Independent Test**: Place a call and verify: (1) emotion labels/markers appear for new transcript entries for both speakers, (2) two separate emotion graphs update in real time (operator and customer), (3) after the call ends, the historical detail view shows both emotion timelines and operator trait scores.

### Backend: Models + Services

- [x] T140 [P] Add `EmotionResult` + `EmotionLabel` models and update `TranscriptEntry` to include optional `Emotion` in `ContactCenter-API/Models/TranscriptEntry.cs` (or a new `EmotionResult.cs`). Default to `Neutral` + confidence 0 when unavailable.
- [x] T141 [P] Implement `EmotionAnalysisService` in `ContactCenter-API/Services/EmotionAnalysisService.cs`: classify a segment into an `EmotionLabel` with `{label, confidence}` using Azure OpenAI chat completions. Use the same auth/config pattern as `SentimentAnalysisService` (Managed Identity when no key). On error, return Neutral (confidence 0).
- [x] T142 [P] Add `OperatorStyleTraits` value object (Empathy/Energy scores) to `ContactCenter-API/Models/CallRecord.cs` and implement a small `OperatorStyleAnalysisService` that computes these traits at end-of-call from the operator-side (AI) transcript.

### Backend: Real-Time Events

- [x] T143 Add a new SignalR server-to-client event `EmotionUpdate` (similar to `SentimentUpdate`) that sends `{ callConnectionId, entryTimestamp, emotion }` so the frontend can update an already-rendered transcript entry and graphs asynchronously.
- [x] T144 Update `ContactCenter-API/Services/AzureOpenAIService.cs`: after each transcript entry is created, trigger emotion analysis asynchronously and publish `EmotionUpdate` to the call group.

### Backend: History Persistence

- [x] T145 Update end-of-call persistence (`CallService` / `CallbackController` disconnect path) to:
  - Persist per-entry `Emotion` values into `CallRecord.TranscriptEntries`
  - Compute and persist `CallRecord.OperatorStyleTraits` from operator-side transcript

### Frontend: Two Emotion Graphs

- [x] T146 Update `ContactCenter-APP/Pages/Index.cshtml` to render **two** emotion graphs in the live call workspace: **Operator Emotion** and **Customer Emotion**.
- [x] T147 Update `ContactCenter-APP/wwwroot/js/site.js` to handle `EmotionUpdate` and update both graphs based on `TranscriptEntry.Speaker` (operator = AI, customer = recipient). Ensure historical detail view also renders both emotion timelines from persisted history.

### Tests

- [x] T148 [P] Add unit tests for `EmotionAnalysisService` and operator trait computation in `CallCenterPOC-API.Tests/Unit/` (mirroring `SentimentAnalysisTests` patterns). Include a test for graceful Neutral fallback on AOAI errors.
- [x] T149 Update quickstart.md: document emotion analysis (same Azure OpenAI deployment as sentiment), the dual-speaker emotion graph UI, and operator style traits displayed in historical call detail.

---

## Phase 33: Quick Wins + Settings Overlay (US13 + US14)

**Purpose**: Five quick wins from code analysis (Swagger guard, health check, shared base class, pagination, post-call summary) plus a settings overlay with configurable max call time and Voice API selector.

**Independent Test**: Verify Swagger is not accessible in production. Hit `/healthz` â€” returns 200. Open call history â€” paginated (20 per page). Place a call â€” auto-terminates at configured max time (default 2 min). Click the gear icon â€” settings overlay opens. Change max call time to 3 min â€” new calls respect it. After call ends â€” summary appears in historical detail.

### Quick Win 1: Swagger Production Guard

- [x] T150 [FR-068] Update `ContactCenter-API/Program.cs`: re-enable the `if (app.Environment.IsDevelopment())` guard around `app.UseSwagger()` and `app.UseSwaggerUI()`. Un-comment the conditional that was commented out during debugging.

### Quick Win 2: Health Check Endpoint

- [x] T151 [FR-063] Update `ContactCenter-API/Program.cs`: add `app.MapGet("/healthz", () => Results.Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow }))` before `app.Run()`.

### Quick Win 3: Shared Analysis Base Class

- [x] T152 [FR-067] Create `ContactCenter-API/Services/AzureOpenAIAnalysisBase.cs`: abstract base class with Azure OpenAI HTTP client setup (endpoint, auth via `DefaultAzureCredential` or API key), shared `CallChatCompletionAsync(systemPrompt, userMessage, maxTokens, reasoningEffort)` method with primary/legacy parameter retry logic, and JSON response extraction. Move the duplicated fields (`_endpointUri`, `_apiKey`, `_deployment`, `_credential`, `HttpClient`, constants) from the three analysis services into this base.
- [x] T153 [P] [FR-067] Refactor `ContactCenter-API/Services/SentimentAnalysisService.cs` to inherit from `AzureOpenAIAnalysisBase`. Remove duplicated auth/HTTP code. Keep sentiment-specific prompt and result parsing.
- [x] T154 [P] [FR-067] Refactor `ContactCenter-API/Services/EmotionAnalysisService.cs` to inherit from `AzureOpenAIAnalysisBase`. Remove duplicated auth/HTTP code. Keep emotion-specific prompt and result parsing.
- [x] T155 [P] [FR-067] Refactor `ContactCenter-API/Services/OperatorStyleAnalysisService.cs` to inherit from `AzureOpenAIAnalysisBase`. Remove duplicated auth/HTTP code. Keep operator-traits-specific prompt and result parsing.

### Quick Win 4: Call History Pagination

- [x] T156 [FR-064] Update `ContactCenter-API/Services/CallHistoryService.cs`: add `GetPagedAsync(int page, int pageSize)` method that returns `{ totalCount, page, pageSize, items[] }`. Builds on existing `GetAllAsync()` cached summaries.
- [x] T157 [FR-064] Update `ContactCenter-API/Controllers/CallHistoryController.cs`: update `GET /api/CallHistory` to accept `page` (default 1) and `pageSize` (default 20) query parameters. Return paginated response wrapper.
- [x] T158 [FR-064] Update `ContactCenter-APP/wwwroot/js/site.js`: update `loadCallHistory()` to pass `page`/`pageSize` parameters. Add "Load More" button or page navigation in the historical calls tab.

### Quick Win 5: Post-Call Summary Generation

- [x] T159 [FR-066] [US14] Add `CallSummary` (string?) and `SummarizedAt` (DateTimeOffset?) fields to `ContactCenter-API/Models/CallRecord.cs`.
- [x] T160 [FR-066] [US14] Create `ContactCenter-API/Services/CallSummaryService.cs`: inherits from `AzureOpenAIAnalysisBase`. Method `GenerateSummaryAsync(List<TranscriptEntry> entries)` sends transcript to Azure OpenAI with a summarization prompt and returns a 2-4 sentence summary string.
- [x] T161 [FR-066] [US14] Update call disconnect persistence in `CallService` or `CallbackController`: after building the `CallRecord`, call `CallSummaryService.GenerateSummaryAsync()` and set `CallRecord.CallSummary`. Fire-and-forget with error logging (don't block disconnect).
- [x] T162 [US14] Update `ContactCenter-APP/wwwroot/js/site.js`: display `CallSummary` in the historical detail center panel, above the transcript, in a styled summary card.

### Settings Overlay â€” Backend

- [x] T163 [FR-065] Create `ContactCenter-API/Models/Settings.cs`: `OperatorSettings` model with `MaxCallTimeMinutes` (int, default 2), `VoiceApi` (string, default "chatgpt-realtime"), `SelectedVoice` (string, default "alloy"). Valid voice values: alloy, echo, fable, onyx, nova, shimmer.
- [x] T164 [FR-065] Create `ContactCenter-API/Services/SettingsService.cs`: singleton service that reads/writes `settings.json` in the configured Blob container. Methods: `GetSettingsAsync()`, `SaveSettingsAsync(OperatorSettings)`. Cache in memory, invalidate on save.
- [x] T165 [FR-065] Create `ContactCenter-API/Controllers/SettingsController.cs`: `GET /api/Settings` returns current settings. `PUT /api/Settings` accepts updated settings body and persists via `SettingsService`.
- [x] T166 [FR-061] Update `ContactCenter-API/Services/CallService.cs`: inject `SettingsService`. In `InitiateCall()`, read `MaxCallTimeMinutes` from settings instead of hardcoded `TimeSpan.FromMinutes(5)`. Default to 2 minutes if settings unavailable.
- [x] T167 Register `CallSummaryService` and `SettingsService` as singletons in `ContactCenter-API/Program.cs`.

### Settings Overlay â€” Frontend

- [x] T168 [FR-060] Update `ContactCenter-APP/Pages/Shared/_Layout.cshtml`: add a gear icon (SVG) to the right side of the header bar, before the "Operations Dashboard" badge. Wire `onclick` to toggle settings overlay.
- [x] T169 [FR-060] Update `ContactCenter-APP/Pages/Index.cshtml`: add settings overlay HTML â€” a right-side sliding panel with dark theme. Contains: Max Call Time slider/input (1-10 min), Voice API selector (radio buttons), AI Voice dropdown (Alloy/Echo/Fable/Onyx/Nova/Shimmer), close button.
- [x] T170 [FR-060] [FR-061] [FR-062] [FR-069] Update `ContactCenter-APP/wwwroot/js/site.js`: settings overlay open/close logic, load settings on page load via `GET /api/Settings`, save on change via `PUT /api/Settings`. Disable "Voice Live" option with "Coming Soon" badge. Populate AI Voice dropdown and sync with saved settings.
- [x] T171 Update `ContactCenter-APP/wwwroot/css/site.css`: settings overlay styles (slide-in animation, dark theme matching sidebar, form controls styling, backdrop dimming).

### Tests

- [x] T172 [P] Add `SettingsControllerContractTests` in `CallCenterPOC-API.Tests/Contract/`: test (a) `GET /api/Settings` returns 200 with default settings, (b) `PUT /api/Settings` with valid body returns 200 and persists, (c) `GET /healthz` returns 200 with status "healthy".
- [x] T173 [P] Add `CallSummaryServiceTests` in `CallCenterPOC-API.Tests/Unit/`: test summary generation with mock transcript entries. Test graceful null return on AOAI errors.
- [x] T174 [P] Update `CallHistoryContractTests`: test pagination â€” verify `GET /api/CallHistory?page=1&pageSize=5` returns at most 5 items with `totalCount`.
- [x] T175 Verify all existing tests still pass after base class refactoring (SentimentAnalysisTests, EmotionAnalysisTests, CampaignServiceTests).

### AI Voice Selection (FR-069)

- [x] T176 [FR-069] Update `ContactCenter-API/Models/OperatorSettings.cs`: add `SelectedVoice` property (string, default "alloy"). Valid values: alloy, echo, fable, onyx, nova, shimmer.
- [x] T177 [FR-069] Update `ContactCenter-API/Services/SettingsService.cs`: validate `SelectedVoice` on save â€” if the value is not one of the 6 valid voices, default to "alloy".
- [x] T178 [FR-069] Update `ContactCenter-API/Services/AzureOpenAIService.cs`: accept selected voice from settings. In `CreateAISessionAsync()`, map the string voice name to `ConversationVoice` (Alloy/Echo/Fable/Onyx/Nova/Shimmer). Replace the hardcoded `ConversationVoice.Alloy`.
- [x] T179 [FR-069] Update `ContactCenter-API/Services/CallService.cs` or `AcsMediaStreamingHandler`: pass the selected voice from `SettingsService` through to `AzureOpenAIService` when creating the AI session.
- [x] T180 [FR-069] Update `ContactCenter-APP/Pages/Index.cshtml`: add AI Voice dropdown (`<select>`) to the settings overlay with options for all 6 voices.
- [x] T181 [FR-069] Update `ContactCenter-APP/wwwroot/js/site.js`: populate AI Voice dropdown from saved settings on load, include `selectedVoice` in the `PUT /api/Settings` payload on save.
- [x] T182 [FR-069] Add tests: (a) SettingsControllerContractTests â€” PUT with valid voice returns 200, PUT with invalid voice defaults to alloy. (b) Verify GET returns `selectedVoice` field.

**Checkpoint**: Swagger restricted to dev. Health check available. Analysis services share base class. History paginated. Post-call summary generated. Settings overlay functional with max call time (2 min default), Voice API selector, and AI Voice dropdown (6 voices, default Alloy).

