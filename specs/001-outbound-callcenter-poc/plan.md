# Implementation Plan: Outbound Call Center POC

**Branch**: `001-outbound-callcenter-poc` | **Date**: 2026-02-20 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/001-outbound-callcenter-poc/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Build an outbound call center POC with two .NET 9 projects: a Razor Pages web app (operator UI) and an ASP.NET Core Web API (call orchestration). The operator uses a professional, enterprise-grade operations center dashboard inspired by modern banking/financial services UIs — deep navy branded header, three-panel layout (left: campaign library with colored category badges + call setup; center: live call workspace with professional call controls, chat-bubble transcript, sentiment graph, and quick responses; right: call analytics and history with expandable detail views), and KPI summary cards. The system ships with 6 pre-defined outbound campaigns (Bank Loan Collection, New Product Marketing, Customer Satisfaction Survey, Appointment Reminder, Insurance Policy Renewal, Subscription Renewal & Upsell), each with detailed production-quality AI behavior instructions. The API places simultaneous outbound calls via Azure Communication Services Call Automation, establishes independent bidirectional audio WebSockets, and bridges each to its own Azure OpenAI Realtime API session for live AI-driven voice conversations. Calls are auto-recorded, capped at 5 minutes and 5 concurrent, with live transcripts and real-time sentiment analysis streamed back to the operator. Campaigns are persistable and customizable. Call history with full transcripts, sentiment analytics, speaker timelines, and on-demand transcription of previously stored recordings is available for post-call review.

## Technical Context

**Language/Version**: C# / .NET 9.0  
**Primary Dependencies**: Azure.Communication.CallAutomation 1.4.0-beta.1, Azure.AI.OpenAI 2.1.0-beta.2 (OpenAI.RealtimeConversation), Azure.Messaging.EventGrid 4.29.0, Azure.Identity, Azure.Storage.Blobs, Swashbuckle.AspNetCore 6.6.2, Newtonsoft.Json 13.0.3  
**Storage**: Azure Blob Storage (call recordings via ACS managed recording + transcript/campaign JSON persistence)  
**Testing**: xUnit + Microsoft.AspNetCore.Mvc.Testing (WebApplicationFactory) + Moq — see [research.md](research.md) §1  
**Target Platform**: Windows/Linux server (ASP.NET Core), modern desktop browser (Chrome/Edge)  
**Project Type**: Web — Razor Pages frontend + Web API backend (two separate .NET projects in one solution)  
**Performance Goals**: <2s AI response latency (SC-002), <60s end-to-end call initiation (SC-001)  
**Constraints**: 5-minute max call duration (FR-016), 5 concurrent calls max (FR-017)  
**Scale/Scope**: Single operator, POC-level (no auth). Persistence: recordings (ACS Blob), transcripts + campaigns (Blob JSON files), call history (Blob JSON)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| # | Principle | Status | Notes |
|---|-----------|--------|-------|
| I | POC-First Simplicity | ✅ PASS | Two projects match the existing repo. No over-engineering. |
| II | Azure-First | ✅ PASS | ACS Call Automation + Azure OpenAI already in use. |
| III | No Secrets in Git | ✅ PASS | Both projects use UserSecretsId; appsettings.json has "xx" placeholders. |
| — | Security: phone PII in logs | ✅ JUSTIFIED | Phone numbers are logged unmasked for POC debugging. Acceptable because: (a) POC is not production, (b) phone numbers are the primary call identifier, (c) no log forwarding to external systems. |
| IV | Observable-by-Default | ✅ PASS | Existing code logs call lifecycle events. Plan adds structured correlation IDs. |
| V | Tests Where They Pay Off | ⚠️ VIOLATION | No test project exists. Constitution requires unit tests for validation logic and contract tests for HTTP endpoints. |
| — | Security: recordings | ⚠️ CONFLICT | Constitution: "Do not persist recordings unless explicitly enabled." Spec FR-009/FR-010: auto-record. See Complexity Tracking below for justification. |
| — | Workflow: OpenAPI contracts | ✅ PASS | Plan Phase 1 produces OpenAPI contracts in `specs/*/contracts/`. |
| — | Workflow: quickstart.md | ✅ PASS | Plan Phase 1 produces quickstart.md with all config keys. |

**Gate result**: PASS WITH JUSTIFIED VIOLATIONS (2 items tracked below).

### Post-Design Re-Evaluation (Phase 1 Complete)

| # | Principle | Status | Resolution |
|---|-----------|--------|------------|
| V | Tests Where They Pay Off | ✅ RESOLVED | Test project `CallCenterPOC-API.Tests` defined in project structure with xUnit + WebApplicationFactory |
| — | Security: recordings | ✅ RESOLVED | Justified; `BlobContainer` config can be left empty to disable; authenticated Azure Blob Storage |

**Post-design gate result**: ✅ ALL PASS

## Project Structure

### Documentation (this feature)

```text
specs/001-outbound-callcenter-poc/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (OpenAPI spec)
│   └── api.yaml
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
CallCenterPOC-App/                    # Frontend — ASP.NET Core Razor Pages
├── Program.cs                        # App startup
├── appsettings.json                  # Non-secret config (ApiBaseUrl)
├── Pages/
│   ├── Index.cshtml                  # Three-panel operations center dashboard
│   ├── Index.cshtml.cs               # Page model (form post → API call, SignalR for transcript)
│   ├── Shared/_Layout.cshtml         # Layout (simplified nav for single-page dashboard)
│   └── ...
└── wwwroot/
    ├── js/site.js                    # Client-side: dashboard panels, live transcript, sentiment graph, call history, SignalR
    └── css/site.css                  # Styling: three-panel layout, dark theme, sentiment graph, call history inline

ContactCenterPOC-API/                 # Backend — ASP.NET Core Web API
├── Program.cs                        # App startup, DI, middleware
├── appsettings.json                  # Non-secret config (ACS, OpenAI, BlobContainer, CallbackUrl)
├── Controllers/
│   ├── CallController.cs             # POST /api/Call/initiate, GET /api/Call/active, POST /api/Call/hangup
│   ├── CallbackController.cs         # POST /api/Callback — ACS event callbacks; GET /api/Callback/ws — media WS
│   ├── CampaignController.cs         # GET/POST /api/Campaign — campaign CRUD
│   ├── CallHistoryController.cs      # GET /api/CallHistory — call history & detail
│   └── TestController.cs             # POST /api/Test/test-call — dev shortcut
├── Models/
│   ├── CallbackEventModels.cs        # CallbackEvent, CallRequest DTOs
│   ├── Campaign.cs                   # Campaign entity
│   ├── CallRecord.cs                 # Persisted call record for history
│   ├── SentimentResult.cs            # Sentiment analysis result
│   └── acsMediaStreamingHandler.cs   # ACS ↔ OpenAI audio bridge (WebSocket)
├── Services/
│   ├── CallService.cs                # Call lifecycle orchestration
│   ├── AzureOpenAIService.cs         # OpenAI Realtime session management
│   ├── SentimentAnalysisService.cs   # Real-time sentiment via Azure OpenAI
│   ├── CampaignService.cs            # Campaign CRUD + persistence
│   └── CallHistoryService.cs         # Call record persistence + retrieval
└── Hubs/
    └── TranscriptHub.cs              # SignalR hub for live transcript + sentiment

CallCenterPOC-API.Tests/              # Test project (Constitution V)
├── Unit/
│   ├── PhoneNumberValidationTests.cs
│   ├── ConcurrentCallLimitTests.cs
│   ├── SentimentAnalysisTests.cs
│   └── CampaignServiceTests.cs
└── Contract/
    ├── CallControllerContractTests.cs
    ├── CampaignControllerContractTests.cs
    └── CallHistoryContractTests.cs
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
2. **Sentiment Fix (US5)**: Configure missing `AzureOpenAI__ChatDeployment` app setting on Azure so sentiment analysis works in production.
3. **Contact Name (US10)**: Add optional contact name fields next to phone number inputs, pass name into AI prompt for personalized greetings, persist name in call history.

### Technical Approach

**Recording Playback**:
- ACS stores recordings in Azure Blob Storage using `RecordingStorage.CreateAzureBlobContainerRecordingStorage`. The recording content can be downloaded via the ACS `CallRecording.DownloadToAsync()` API using the recording ID.
- Add `RecordingId` field to `CallRecord` model. Populate it from `ActiveCall.RecordingId` when persisting on disconnect.
- Add `GET /api/CallHistory/{callConnectionId}/recording` endpoint that downloads the recording from ACS and streams it to the client as `audio/mpeg`.
- In the frontend call detail panel, render an `<audio>` element with `src` pointing to the recording endpoint when a recording ID exists.

**Sentiment Fix**:
- Root cause: `AzureOpenAI__ChatDeployment` app setting is missing from the Azure App Service for `contactcenterpoc-api`. The `SentimentAnalysisService` constructor sets `_chatClient = null` when `ChatDeployment` config is missing, causing all sentiment to return Neutral with 0 confidence.
- Fix: Run `az webapp config appsettings set` to add `AzureOpenAI__ChatDeployment=gpt-4o-mini`.
- No code changes needed — the sentiment infrastructure (SignalR events, UI dot matching, graph rendering) is already fully implemented.

**Contact Name**:
- Add `ContactNames` (string[]?) to `CallRequest` DTO, parallel to `PhoneNumbers`.
- Add `ContactName` (string?) to `ActiveCall` model.
- In `CallService.InitiateCall()`, prepend contact name instruction to the AI prompt: `"The person you are calling is named {name}. Greet them by name."`.
- Add `ContactName` to `CallRecord` for history persistence.
- Update frontend: add name input field next to each phone number input, send names in API request, display name in call history.

### Files Changed

| File | Change |
|------|--------|
| `ContactCenterPOC-API/Models/CallRecord.cs` | Add `RecordingId`, `ContactName` fields |
| `ContactCenterPOC-API/Models/ActiveCall.cs` | Add `ContactName` field |
| `ContactCenterPOC-API/Models/CallbackEventModels.cs` | Add `ContactNames` to `CallRequest` |
| `ContactCenterPOC-API/Controllers/CallHistoryController.cs` | Add `GET {id}/recording` endpoint |
| `ContactCenterPOC-API/Services/CallService.cs` | Pass contact name into prompt, persist `RecordingId` + `ContactName` |
| `ContactCenterPOC-API/Services/CallHistoryService.cs` | Include `ContactName` in summary |
| `CallCenterPOC-App/Pages/Index.cshtml` | Add contact name inputs, audio player in detail panel |
| `CallCenterPOC-App/wwwroot/js/site.js` | Send contact names, render audio player, show names in history |
| `CallCenterPOC-App/wwwroot/css/site.css` | Styles for contact name inputs and audio player |
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
- The existing `CallHistoryService` code already persists to Blob — it just lacks a working `BlobServiceClient` connection.

**Recording Playback Fix**:
- Replace the `DownloadStreamingAsync(Uri)` approach in `CallHistoryController.GetRecording()`.
- Inject `BlobServiceClient` + read `BlobContainer` config to get the recording container name.
- Search for blobs in the recording container with a prefix matching the recording ID.
- Stream the first matching MP3 blob directly to the client.
- Add `HasRecording` to `CallHistorySummary` so the UI can show a recording indicator on history items.

**Campaign Prompt UX Fix**:
- Update `selectCampaign()` to always clear the prompt override textarea and show the campaign instructions in a dedicated read-only preview area.
- Add a "Campaign Prompt" preview section below the campaign cards that shows the selected campaign's `aiBehaviorInstructions`.
- In `initiateCall()`, only include `prompt` in the body if the user explicitly typed in the override area — not if it was auto-populated.
- On the API side, the existing logic already handles `prompt` vs `campaignId` priority correctly; the fix is purely frontend.

### Files Changed

| File | Change |
|------|--------|
| `ContactCenterPOC-API/Program.cs` | Derive `BlobStorage:AccountUri` from `BlobContainer` as fallback |
| `ContactCenterPOC-API/Controllers/CallHistoryController.cs` | Download recording from Blob container instead of ACS API |
| `ContactCenterPOC-API/Models/CallRecord.cs` | Add `HasRecording` to `CallHistorySummary` |
| `ContactCenterPOC-API/Services/CallHistoryService.cs` | Set `HasRecording` in `ToSummary()` |
| `CallCenterPOC-App/Pages/Index.cshtml` | Add campaign prompt preview area |
| `CallCenterPOC-App/wwwroot/js/site.js` | Fix campaign selection, recording icon, prompt handling |
| `CallCenterPOC-App/wwwroot/css/site.css` | Styles for prompt preview area |

---

## Phase 18: Transcribe Existing Recordings Into History (US11)

### Summary

Add an on-demand transcription flow for recordings that already exist in Blob Storage, and persist the transcript back into the historical call record JSON. This enables historical-call review with a readable transcript even if it was not captured live.

### Technical Approach

- Implement a backend `RecordingTranscriptionService` that downloads the recording audio from the configured ACS recording container in Blob Storage and calls the Azure OpenAI audio transcription endpoint.
- Authenticate to Azure OpenAI via Entra ID using `DefaultAzureCredential` and request a Bearer token scoped to `https://cognitiveservices.azure.com/.default`.
- Persist the resulting transcript text into the existing call history blob `call-history/{callConnectionId}.json` (single source of truth).
- Expose `POST /api/CallHistory/{callConnectionId}/transcribe?force=false` to trigger transcription and return the updated `CallRecord`.
- In the dashboard call detail panel, show a “Transcribe” button when a recording exists but no transcript is present; display the transcript and hide the button once persisted.
