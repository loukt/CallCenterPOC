# Implementation Plan: Outbound Call Center POC

**Branch**: `001-outbound-callcenter-poc` | **Date**: 2026-02-20 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/001-outbound-callcenter-poc/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Build an outbound call center POC with two .NET 9 projects: a Razor Pages web app (operator UI) and an ASP.NET Core Web API (call orchestration). The operator uses a three-panel operations dashboard (left: campaign library + call setup; center: live call workspace; right: lists and navigation). The API places simultaneous outbound calls via Azure Communication Services Call Automation, establishes independent bidirectional audio WebSockets, and bridges each to its own Azure OpenAI Realtime API session for live AI-driven voice conversations. Calls are auto-recorded, capped at 5 minutes and 5 concurrent, with live transcripts and real-time sentiment analysis streamed back to the operator. Campaigns persist in Azure Blob Storage.

Historical calls are persisted as JSON blobs under `call-history/` and are resilient to refresh/restart via (a) immediate persistence at call initiation, (b) disconnect-time persistence, (c) short-lived summary caching with TTL, and (d) automatic rebuild of missing history from ACS recording metadata (`0-acsmetadata.json`) when the `call-history/` prefix is empty. Recordings can be played back from the dashboard with HTTP range support, and operators can trigger on-demand transcription of existing recordings, persisting `recordingTranscript` into the call record JSON.

UX: the right pane has **two tabs** â€” **Live calls** and **Historical calls**. Selecting a call from either list shows/focuses its details in the **center panel**. Selecting/starting live calls switches the center back to live operations.

## Technical Context

**Language/Version**: C# / .NET 9.0  
**Primary Dependencies**: Azure.Communication.CallAutomation 1.4.0-beta.1, Azure.AI.OpenAI 2.1.0-beta.2 (OpenAI.RealtimeConversation), Azure.Messaging.EventGrid 4.29.0, Azure.Identity, Azure.Storage.Blobs, Swashbuckle.AspNetCore 6.6.2, Newtonsoft.Json 13.0.3  
**Storage**: Azure Blob Storage (call recordings via ACS managed recording + transcript/campaign JSON persistence)  
**Testing**: xUnit + Microsoft.AspNetCore.Mvc.Testing (WebApplicationFactory) + Moq â€” see [research.md](research.md) Â§1  
**Target Platform**: Windows/Linux server (ASP.NET Core), modern desktop browser (Chrome/Edge)  
**Project Type**: Web â€” Razor Pages frontend + Web API backend (two separate .NET projects in one solution)  
**Performance Goals**: <2s AI response latency (SC-002), <60s end-to-end call initiation (SC-001)  
**Constraints**: 5-minute max call duration (FR-016), 5 concurrent calls max (FR-017)  
**Scale/Scope**: Single operator, POC-level (no auth). Persistence: recordings (ACS Blob), transcripts + campaigns (Blob JSON files), call history (Blob JSON)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Status | Notes |
|---|-----------|--------|-------|
| I | POC-First Simplicity | âœ… PASS | Two projects match the existing repo. No over-engineering. |
| II | Azure-First | âœ… PASS | ACS Call Automation + Azure OpenAI already in use. |
| III | No Secrets in Git | âœ… PASS | Both projects use UserSecretsId; appsettings.json has "xx" placeholders. |
| â€” | Security: phone PII in logs | âœ… JUSTIFIED | Phone numbers are logged unmasked for POC debugging. Acceptable because: (a) POC is not production, (b) phone numbers are the primary call identifier, (c) no log forwarding to external systems. |
| IV | Observable-by-Default | âœ… PASS | Existing code logs call lifecycle events. Plan adds structured correlation IDs. |
| V | Tests Where They Pay Off | âš ï¸ VIOLATION | No test project exists. Constitution requires unit tests for validation logic and contract tests for HTTP endpoints. |
| â€” | Security: recordings | âš ï¸ CONFLICT | Constitution: "Do not persist recordings unless explicitly enabled." Spec FR-009/FR-010: auto-record. See Complexity Tracking below for justification. |
| â€” | Workflow: OpenAPI contracts | âœ… PASS | Plan Phase 1 produces OpenAPI contracts in `specs/*/contracts/`. |
| â€” | Workflow: quickstart.md | âœ… PASS | Plan Phase 1 produces quickstart.md with all config keys. |

**Gate result**: PASS WITH JUSTIFIED VIOLATIONS (2 items tracked below).

### Post-Design Re-Evaluation (Phase 1 Complete)

| # | Principle | Status | Resolution |
|---|-----------|--------|------------|
| V | Tests Where They Pay Off | âœ… RESOLVED | Test project `CallCenterPOC-API.Tests` defined in project structure with xUnit + WebApplicationFactory |
| â€” | Security: recordings | âœ… RESOLVED | Justified; `BlobContainer` config can be left empty to disable; authenticated Azure Blob Storage |

**Post-design gate result**: âœ… ALL PASS

## Project Structure

### Documentation (this feature)

```text
specs/001-outbound-callcenter-poc/
â”œâ”€â”€ plan.md              # This file
â”œâ”€â”€ research.md          # Phase 0 output
â”œâ”€â”€ data-model.md        # Phase 1 output
â”œâ”€â”€ quickstart.md        # Phase 1 output
â”œâ”€â”€ contracts/           # Phase 1 output (OpenAPI spec)
â”‚   â””â”€â”€ api.yaml
â””â”€â”€ tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
ContactCenter-APP/                    # Frontend â€” ASP.NET Core Razor Pages
â”œâ”€â”€ Program.cs                        # App startup
â”œâ”€â”€ appsettings.json                  # Non-secret config (ApiBaseUrl)
â”œâ”€â”€ Pages/
â”‚   â”œâ”€â”€ Index.cshtml                  # Three-panel operations center dashboard
â”‚   â”œâ”€â”€ Index.cshtml.cs               # Page model (form post â†’ API call, SignalR for transcript)
â”‚   â”œâ”€â”€ Shared/_Layout.cshtml         # Layout (simplified nav for single-page dashboard)
â”‚   â””â”€â”€ ...
â””â”€â”€ wwwroot/
    â”œâ”€â”€ js/site.js                    # Client-side: dashboard panels, live transcript, sentiment graph, call history, SignalR
    â””â”€â”€ css/site.css                  # Styling: three-panel layout, dark theme, sentiment graph, call history inline

ContactCenter-API/                 # Backend â€” ASP.NET Core Web API
â”œâ”€â”€ Program.cs                        # App startup, DI, middleware
â”œâ”€â”€ appsettings.json                  # Non-secret config (ACS, OpenAI, BlobContainer, CallbackUrl)
â”œâ”€â”€ Controllers/
â”‚   â”œâ”€â”€ CallController.cs             # POST /api/Call/initiate, GET /api/Call/active, POST /api/Call/hangup
â”‚   â”œâ”€â”€ CallbackController.cs         # POST /api/Callback â€” ACS event callbacks; GET /api/Callback/ws â€” media WS
â”‚   â”œâ”€â”€ CampaignController.cs         # GET/POST /api/Campaign â€” campaign CRUD
â”‚   â”œâ”€â”€ CallHistoryController.cs      # GET /api/CallHistory â€” call history & detail
â”‚   â””â”€â”€ TestController.cs             # POST /api/Test/test-call â€” dev shortcut
â”œâ”€â”€ Models/
â”‚   â”œâ”€â”€ CallbackEventModels.cs        # CallbackEvent, CallRequest DTOs
â”‚   â”œâ”€â”€ Campaign.cs                   # Campaign entity
â”‚   â”œâ”€â”€ CallRecord.cs                 # Persisted call record for history
â”‚   â”œâ”€â”€ SentimentResult.cs            # Sentiment analysis result
â”‚   â””â”€â”€ acsMediaStreamingHandler.cs   # ACS â†” OpenAI audio bridge (WebSocket)
â”œâ”€â”€ Services/
â”‚   â”œâ”€â”€ CallService.cs                # Call lifecycle orchestration
â”‚   â”œâ”€â”€ AzureOpenAIService.cs         # OpenAI Realtime session management
â”‚   â”œâ”€â”€ SentimentAnalysisService.cs   # Real-time sentiment via Azure OpenAI
â”‚   â”œâ”€â”€ CampaignService.cs            # Campaign CRUD + persistence
â”‚   â””â”€â”€ CallHistoryService.cs         # Call record persistence + retrieval
â””â”€â”€ Hubs/
    â””â”€â”€ TranscriptHub.cs              # SignalR hub for live transcript + sentiment

CallCenterPOC-API.Tests/              # Test project (Constitution V)
â”œâ”€â”€ Unit/
â”‚   â”œâ”€â”€ PhoneNumberValidationTests.cs
â”‚   â”œâ”€â”€ ConcurrentCallLimitTests.cs
â”‚   â”œâ”€â”€ SentimentAnalysisTests.cs
â”‚   â””â”€â”€ CampaignServiceTests.cs
â””â”€â”€ Contract/
    â”œâ”€â”€ CallControllerContractTests.cs
    â”œâ”€â”€ CampaignControllerContractTests.cs
    â””â”€â”€ CallHistoryContractTests.cs
```

**Structure Decision**: Existing two-project web structure (Razor Pages frontend + Web API backend) is retained. New controllers (`CampaignController`, `CallHistoryController`), services (`SentimentAnalysisService`, `CampaignService`, `CallHistoryService`), and models (`Campaign`, `CallRecord`, `SentimentResult`) are added for Phase 2 features. The frontend is redesigned as an enterprise-grade operations center dashboard (US8) with deep navy theme, professional call controls, chat-bubble transcript, rich campaign cards with colored category badges, and KPI summary cards. The system ships 6 outbound-focused default campaigns. Call history is embedded in the right panel instead of a separate page. Test project expanded with tests for sentiment and campaign logic.

## Complexity Tracking

> **Justified violations from Constitution Check**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| No test project (Principle V) | Constitution requires unit tests for validation and contract tests for endpoints | Will add `CallCenterPOC-API.Tests` with targeted tests for phone validation, concurrent call limits, and call initiation endpoint |
| Auto-recording conflicts with "opt-in only" security constraint | Spec FR-009/FR-010 explicitly require automatic recording; this was a clarified requirement from the user | Making recording opt-in would contradict the user's spec. Mitigation: recordings stored in authenticated Azure Blob Storage; blob container URL is a config setting (can be left empty to disable). |
| Transcript persistence conflicts with "opt-in only" security constraint | Spec FR-020 explicitly requires persisting transcripts with sentiment data for post-call review; this was a clarified requirement from the user (session 2026-02-21) | Not persisting would make Call History (US7) impossible. Mitigation: transcripts stored as authenticated Blob JSON files; no PII beyond phone numbers (already justified). |
| Phone numbers logged unmasked (Principle III: PII) | Phone numbers serve as the primary call identifier for POC debugging; masking would make call tracing impossible | Not a production system; no log forwarding to external services. Acceptable for POC scope. |
| Recording playback proxied through API | Direct Blob Storage SAS URLs expose storage structure | Proxying through `/api/CallHistory/{id}/recording` keeps storage details internal. Adds minor latency but acceptable for POC. |

---

## Phase 15: Recording Playback, Sentiment Fix, Contact Names (US4 + US5 + US9 + US10)

### Summary

Three features and one bug fix:
1. **Call Recording Playback (US9)**: Add recording URL to `CallRecord`, add API endpoint to download recording audio, add HTML5 audio player in call history detail.
2. **Sentiment Fix (US5)**: Fix sentiment analysis in production by aligning the Azure OpenAI deployment/config and request payload with the deployed model (e.g., `gpt-5-nano`).
3. **Contact Name (US10)**: Add optional contact name fields next to phone number inputs, pass name into AI prompt for personalized greetings, persist name in call history.

### Technical Approach

**Recording Playback**:
- ACS stores recordings in Azure Blob Storage using `RecordingStorage.CreateAzureBlobContainerRecordingStorage`. The recording content can be downloaded via the ACS `CallRecording.DownloadToAsync()` API using the recording ID.
- Add `RecordingId` field to `CallRecord` model. Populate it from `ActiveCall.RecordingId` when persisting on disconnect.
- Add `GET /api/CallHistory/{callConnectionId}/recording` endpoint that downloads the recording from ACS and streams it to the client as `audio/mpeg`.
- In the frontend call detail panel, render an `<audio>` element with `src` pointing to the recording endpoint when a recording ID exists.

**Sentiment Fix**:
- Root causes observed in production:
    - Azure OpenAI **deployment/config mismatches** (deployment name not configured / wrong deployment name).
    - `gpt-5-nano` **rejects some legacy chat-completions parameters** (e.g., uses `max_completion_tokens` rather than `max_tokens`, and may reject `temperature=0`).
- Fix:
    - Update `SentimentAnalysisService` to call the Azure OpenAI **chat completions REST API** with a payload compatible with `gpt-5-nano` (using `max_completion_tokens` and omitting `temperature`), and retry with legacy parameters only when the error indicates unsupported parameters.
    - Support local development via `AzureOpenAI:Key` when available; otherwise authenticate with `DefaultAzureCredential` (Managed Identity / Entra ID) for Azure.


**Contact Name**:
- Add `ContactNames` (string[]?) to `CallRequest` DTO, parallel to `PhoneNumbers`.
- Add `ContactName` (string?) to `ActiveCall` model.
- In `CallService.InitiateCall()`, prepend contact name instruction to the AI prompt: `"The person you are calling is named {name}. Greet them by name."`.
- Add `ContactName` to `CallRecord` for history persistence.
- Update frontend: add name input field next to each phone number input, send names in API request, display name in call history.

### Files Changed

| File | Change |
|------|--------|
| `ContactCenter-API/Models/CallRecord.cs` | Add `RecordingId`, `ContactName` fields |
| `ContactCenter-API/Models/ActiveCall.cs` | Add `ContactName` field |
| `ContactCenter-API/Models/CallbackEventModels.cs` | Add `ContactNames` to `CallRequest` |
| `ContactCenter-API/Controllers/CallHistoryController.cs` | Add `GET {id}/recording` endpoint |
| `ContactCenter-API/Services/CallService.cs` | Pass contact name into prompt, persist `RecordingId` + `ContactName` |
| `ContactCenter-API/Services/CallHistoryService.cs` | Include `ContactName` in summary |
| `ContactCenter-APP/Pages/Index.cshtml` | Add contact name inputs, audio player in detail panel |
| `ContactCenter-APP/wwwroot/js/site.js` | Send contact names, render audio player, show names in history |
| `ContactCenter-APP/wwwroot/css/site.css` | Styles for contact name inputs and audio player |
| `CallCenterPOC-API.Tests/` | Update tests for new fields |

---

## Phase 16: History Persistence, Recording Playback Fix, Campaign Prompt UX (Bug Fixes)

### Summary

Three critical bug fixes identified during production testing:
1. **Call History Not Persistent**: `BlobStorage:AccountUri` was never configured on Azure, causing `BlobServiceClient` to fall back to `UseDevelopmentStorage=true` which silently fails. Campaign list appeared to work because `CampaignService` falls back to in-memory defaults on Blob failure. Fix: derive `BlobStorage:AccountUri` from the existing `BlobContainer` URL and configure on Azure.
2. **Recording Playback Broken**: The `GET /api/CallHistory/{id}/recording` endpoint uses `DownloadStreamingAsync(new Uri(recordingId))` but `RecordingId` from ACS `StartAsync()` is an opaque ID, not a download URL. The download URL is only available from the `RecordingFileStatusUpdated` Event Grid event, which we don't subscribe to. Fix: download recording files directly from the ACS recording Blob container using `BlobServiceClient`.
3. **Campaign Prompt Not Updating**: `selectCampaign()` only sets the prompt textarea if it's currently empty. If the user previously typed text (e.g., "Loan"), subsequent campaign selections don't update the prompt, and the prompt override always wins in the API. Fix: always show campaign prompt in a visible preview, clear the override textarea when selecting a campaign.

### Technical Approach

**History Persistence Fix**:
- In `Program.cs`, derive `BlobStorage:AccountUri` from the `BlobContainer` URL as a fallback (parse the storage account base URL).
- On Azure, explicitly set `BlobStorage__AccountUri` app setting.
- The existing `CallHistoryService` code already persists to Blob â€” it just lacks a working `BlobServiceClient` connection.

**Recording Playback Fix**:
- Replace the `DownloadStreamingAsync(Uri)` approach in `CallHistoryController.GetRecording()`.
- Inject `BlobServiceClient` + read `BlobContainer` config to get the recording container name.
- Search for blobs in the recording container with a prefix matching the recording ID.
- Stream the first matching MP3 blob directly to the client.
- Add `HasRecording` to `CallHistorySummary` so the UI can show a recording indicator on history items.

**Campaign Prompt UX Fix**:
- Update `selectCampaign()` to always clear the prompt override textarea and show the campaign instructions in a dedicated read-only preview area.
- Add a "Campaign Prompt" preview section below the campaign cards that shows the selected campaign's `aiBehaviorInstructions`.
- In `initiateCall()`, only include `prompt` in the body if the user explicitly typed in the override area â€” not if it was auto-populated.
- On the API side, the existing logic already handles `prompt` vs `campaignId` priority correctly; the fix is purely frontend.

### Files Changed

| File | Change |
|------|--------|
| `ContactCenter-API/Program.cs` | Derive `BlobStorage:AccountUri` from `BlobContainer` as fallback |
| `ContactCenter-API/Controllers/CallHistoryController.cs` | Download recording from Blob container instead of ACS API |
| `ContactCenter-API/Models/CallRecord.cs` | Add `HasRecording` to `CallHistorySummary` |
| `ContactCenter-API/Services/CallHistoryService.cs` | Set `HasRecording` in `ToSummary()` |
| `ContactCenter-APP/Pages/Index.cshtml` | Add campaign prompt preview area |
| `ContactCenter-APP/wwwroot/js/site.js` | Fix campaign selection, recording icon, prompt handling |
| `ContactCenter-APP/wwwroot/css/site.css` | Styles for prompt preview area |

---

## Phase 17: Blob Container Fix, Managed-Identity Sentiment, Rolling Window (US5)

### Summary

Three infrastructure/service fixes that harden the sentiment pipeline and blob storage integration:
1. **Blob Container Name Fix**: Correct the recording blob container name mismatch that prevented recording lookups.
2. **Managed Identity Sentiment Auth (FR-047)**: Remove the API-key fallback from `AzureOpenAIService` so sentiment requests always authenticate via `DefaultAzureCredential` (Managed Identity / Entra ID).
3. **Rolling 5-Second Sentiment Window (FR-048)**: Build the sentiment analysis context from transcript entries within the last **5 seconds** instead of sending the full transcript, keeping sentiment reactive.

### Technical Approach

- Fix `BlobContainerClient` construction so the container name matches the ACS recording container.
- In `AzureOpenAIService`, remove the `AzureOpenAI:Key` path and always obtain a Bearer token via `DefaultAzureCredential`.
- In `AzureOpenAIService.FireAndForgetSentiment()`, filter `TranscriptEntries` to the last 5 seconds before building the prompt.

### Files Changed

| File | Change |
|------|--------|
| `ContactCenter-API/Services/CallHistoryService.cs` | Fix recording blob container name |
| `ContactCenter-API/Services/AzureOpenAIService.cs` | Remove API-key fallback; always use Managed Identity; rolling 5 s window |
| `ContactCenter-API/Services/SentimentAnalysisService.cs` | Auth via DefaultAzureCredential (Managed Identity); optional API key for local dev |
| `ContactCenter-API/appsettings.json` | Remove `AzureOpenAI:Key` placeholder |

---

## Phase 18: Transcribe Existing Recordings Into History (US11)

### Summary

Add an on-demand transcription flow for recordings that already exist in Blob Storage, and persist the transcript back into the historical call record JSON. This enables historical-call review with a readable transcript even if it was not captured live.

### Technical Approach

- Implement a backend `RecordingTranscriptionService` that downloads the recording audio from the configured ACS recording container in Blob Storage and calls the Azure OpenAI audio transcription endpoint.
- Authenticate to Azure OpenAI via Entra ID using `DefaultAzureCredential` and request a Bearer token scoped to `https://cognitiveservices.azure.com/.default`.
- Persist the resulting transcript text into the existing call history blob `call-history/{callConnectionId}.json` (single source of truth).
- Expose `POST /api/CallHistory/{callConnectionId}/transcribe?force=false` to trigger transcription and return the updated `CallRecord`.
- In the dashboard call detail panel, show a â€œTranscribeâ€ button when a recording exists but no transcript is present; display the transcript and hide the button once persisted.

---

## Phase 19: Call History Durability & Playback Reliability (Production Hardening)

### Summary

This phase improves reliability of the historical calls experience in production environments where:
- the API may restart / scale out,
- call disconnect callbacks may be delayed or handled by a different instance,
- history reads are frequent and should not thrash storage,
- older recordings exist in the ACS recording container even if `call-history/` JSON was never created.

### Technical Approach

- **Immediate persistence**: On call initiation, write a minimal `CallRecord` to `call-history/{callConnectionId}.json` so the call appears in history immediately (even before it disconnects).
- **Cache TTL**: Keep an in-memory summary cache with a short TTL (default 5 seconds, configurable via `CallHistory:CacheTtlSeconds`) to avoid stale empties and reduce blob listing pressure.
- **Auto-rebuild when empty**: When there are no `call-history/` blobs, scan for ACS recording chunk metadata (`*/0-acsmetadata.json`) and synthesize minimal `CallRecord` stubs (recordingId, timestamps, extracted PSTN phone number) and persist them under `call-history/`.
- **Recording streaming**: Serve recordings via `FileStreamResult` with `EnableRangeProcessing = true` so the HTML5 `<audio>` player can seek.

### Files Changed (Representative)

| File | Change |
|------|--------|
| `ContactCenter-API/Services/CallService.cs` | Persist initial call record at initiation |
| `ContactCenter-API/Services/CallHistoryService.cs` | Cache TTL + automatic rebuild from ACS metadata |
| `ContactCenter-API/Controllers/CallHistoryController.cs` | Range-enabled recording streaming |

---

## Phase 20: Dashboard Navigation UX (Live vs Historical Tabs)

### Summary

Improve operator workflow clarity by separating *in-progress* calls from *completed* calls in the right pane, and using the center panel as the single primary detail/work surface.

### Technical Approach

- Add right-pane **tabs**: **Live calls** (in-memory active calls) and **Historical calls** (from API).
- Clicking a **historical** item renders recording/transcript/analytics in the **center panel**.
- Clicking a **live** call or starting a new call returns the center panel to **live operations**.

### Files Changed (Representative)

| File | Change |
|------|--------|
| `ContactCenter-APP/Pages/Index.cshtml` | Add right-pane tabs; move history detail panel into center panel |
| `ContactCenter-APP/wwwroot/js/site.js` | Live calls list rendering + center-panel switching logic |

---

## Phase 21: Sentiment Compatibility & Privacy Tweaks

### Summary

Two production UX/privacy tweaks:
1. **Sentiment request compatibility**: use model-compatible chat-completions parameters (`max_completion_tokens`, omit `temperature` for `gpt-5-nano`), and retry with legacy parameters only when the error indicates unsupported parameters.
2. **Phone masking in history**: mask phone numbers in **call history** summaries/details so operators don't see full numbers in historical views.

### Technical Approach

- In `SentimentAnalysisService`, call the Azure OpenAI chat completions REST endpoint with model-compatible parameters (e.g., use `max_completion_tokens` and omit `temperature` for `gpt-5-nano`), and retry with legacy parameters only if the error indicates unsupported parameters.
- Add a small helper (`PhoneNumberMasker`) and apply it in `CallHistoryService` summaries and `CallHistoryController` detail/transcribe responses.
    - Masking is applied **at response time** (does not mutate persisted history blobs).

### Deployment Note

- When deploying to Linux App Service from Windows, prefer zips with POSIX-style entry names (use `scripts/make-posix-zip.ps1`) to avoid path separator issues.

### Files Changed

| File | Change |
|------|--------|
| `ContactCenter-API/Services/SentimentAnalysisService.cs` | Use model-compatible chat-completions parameters; retry with legacy on error |
| `ContactCenter-API/Services/CallHistoryService.cs` | Apply phone masking in summary and detail responses |
| `ContactCenter-API/Controllers/CallHistoryController.cs` | Apply phone masking in transcribe response |
| `ContactCenter-API/Helpers/PhoneNumberMasker.cs` | New: helper to mask phone numbers at response time |

---

## Phase 22: Dual-Speaker Emotion Graphs + Operator Style Traits

### Summary

Add per-speaker emotion detection (operator/AI vs customer/recipient) and visualize it as **two** live graphs in the center panel. Persist the per-segment emotion timeline into call history and compute operator style traits (at minimum: empathy + energy) to display in historical detail.

### Technical Approach

- **Speaker mapping**: reuse existing `TranscriptEntry.Speaker` to split into two streams: operator-side = `AI`, customer-side = `Recipient`.
- **Per-segment emotion**: compute `TranscriptEntry.Emotion = { label, confidence }` asynchronously (similar to sentiment) using Azure OpenAI chat completions.
    - On failures/timeouts, default to `Neutral` with confidence `0` and log without interrupting the call.
- **Live UI**: extend the existing sentiment graph concept to render two emotion graphs (one per speaker). The graphs update on new transcript entries and emotion updates.
- **History persistence**: store emotion results in the persisted `CallRecord.TranscriptEntries` so historical views can render the same two speaker timelines.
- **Operator traits**: on call end (disconnect), analyze the operator-side (AI) transcript as a whole and compute `OperatorStyleTraits` (Empathy/Energy) and persist into the `CallRecord`.

### Files Changed (Expected)

| File | Change |
|------|--------|
| `ContactCenter-API/Models/TranscriptEntry.cs` | Add optional `Emotion` field |
| `ContactCenter-API/Models/CallRecord.cs` | Add `OperatorStyleTraits` field |
| `ContactCenter-API/Services/EmotionAnalysisService.cs` | New: per-segment emotion classification |
| `ContactCenter-API/Services/OperatorStyleAnalysisService.cs` | New: compute operator style traits on call end |
| `ContactCenter-API/Hubs/TranscriptHub.cs` | Broadcast `EmotionUpdate` events to connected clients |
| `ContactCenter-API/Services/CallHistoryService.cs` | Persist/read emotion + operator traits |
| `ContactCenter-APP/Pages/Index.cshtml` | Add two emotion graphs (operator/customer) in live view and historical detail |
| `ContactCenter-APP/wwwroot/js/site.js` | Handle emotion update events and update both graphs |

