# Implementation Plan: Azure AI VoiceLive Integration

**Branch**: `002-voicelive-integration` | **Date**: 2026-03-05 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/002-voicelive-integration/spec.md`

## Summary

Integrate Azure AI VoiceLive as an alternative voice engine alongside the existing OpenAI Realtime API. The operator selects the engine (ChatGPT or VoiceLive), model, voice (Dragon HD, multi-locale), and transcription source via the settings overlay. The backend creates a `VoiceLiveSession` WebSocket instead of an OpenAI Realtime session when VoiceLive is active, reusing the existing ACS media streaming bridge pattern. Reconnection with exponential backoff (up to 3 attempts) and clear UI status indicators are provided for mid-call disconnects. Post-call analysis (sentiment, emotion, summary) works identically regardless of engine.

## Technical Context

**Language/Version**: C# / .NET 9 (net9.0)
**Primary Dependencies**: Azure.AI.OpenAI 2.1.0-beta.2, Azure.Communication.CallAutomation 1.4.0-beta.1, Azure.Identity 1.17.1, Azure.Storage.Blobs 12.23.0, SignalR, **NEW: Azure.AI.VoiceLive 1.0.0 GA**
**Storage**: Azure Blob Storage (JSON blobs for settings, campaigns, call history)
**Testing**: xUnit 2.9.2 + Moq 4.20.72 + Microsoft.AspNetCore.Mvc.Testing (WebApplicationFactory)
**Target Platform**: Azure App Service (Linux), .NET 9 self-contained
**Project Type**: Web application (API backend + Razor Pages frontend)
**Performance Goals**: Audio latency parity with existing ChatGPT Realtime path; <200ms first-audio-byte after speech event
**Constraints**: Max 5 concurrent calls (existing limit); VoiceLive WebSocket reconnection within 7s total (1+2+4 backoff)
**Scale/Scope**: Single-operator POC; ~15 source files modified/added; ~4 new test files

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| **I. POC-First Simplicity** | PASS | VoiceLive handler mirrors existing OpenAI handler pattern. No new abstraction layers. Strategy dispatch via simple `if/switch` on `VoiceApiMode` string — no abstract factory or plugin architecture. |
| **II. Azure-First** | PASS | VoiceLive is an Azure service (Azure AI Speech / Microsoft Foundry). Uses `DefaultAzureCredential` like existing services. |
| **III. No Secrets in Git** | PASS | VoiceLive endpoint URI goes in `appsettings.json`; credentials use Managed Identity or User Secrets. No new secrets in code. |
| **IV. Observable-by-Default** | PASS | FR-015 requires logging engine + voice per call. FR-019 requires reconnection status in UI. Existing `ILogger` patterns reused. |
| **V. Tests Where They Pay Off** | PASS | Unit tests for voice validation/mapping, model validation. Contract tests for settings API changes, health check VoiceLive status. |
| **Security & Compliance** | PASS | No new PII handling. Existing phone masking applies. Transcripts handled same as ChatGPT path. |
| **Dev Workflow: OpenAPI contract** | PASS | Settings endpoint contract updated in `contracts/`. |
| **Dev Workflow: quickstart.md** | PASS | New VoiceLive config keys documented in `quickstart.md`. |
| **Dev Workflow: PR scope** | PASS | All changes under feature `002-voicelive-integration`. |

**Gate result**: ALL PASS — proceed to Phase 0.

## Project Structure

### Documentation (this feature)

```text
specs/002-voicelive-integration/
├── plan.md              # This file
├── research.md          # Phase 0: VoiceLive SDK patterns, WebSocket protocol, Dragon HD voice catalog
├── data-model.md        # Phase 1: Entity definitions, field mapping, state transitions
├── quickstart.md        # Phase 1: Configuration keys, setup steps
├── contracts/           # Phase 1: Updated OpenAPI for Settings, Health endpoints
│   ├── settings-api.yaml
│   └── health-api.yaml
└── tasks.md             # Phase 2 output (not created by /speckit.plan)
```

### Source Code (repository root)

```text
ContactCenter-API/
├── Models/
│   ├── OperatorSettings.cs          # MODIFY: Add TranscriptionMode, VoiceLiveModel, VoiceLive voices
│   ├── ActiveCall.cs                # MODIFY: Add VoiceApiMode tracking per-call
│   ├── acsMediaStreamingHandler.cs  # MODIFY: Dispatch to VoiceLive or OpenAI based on mode
│   └── VoiceLiveVoices.cs           # NEW: Dragon HD voice catalog (multi-locale)
├── Services/
│   ├── AzureOpenAIService.cs        # EXISTING: No changes (OpenAI Realtime path)
│   ├── VoiceLiveService.cs          # NEW: VoiceLive WebSocket session handler (mirrors AzureOpenAIService)
│   ├── SettingsService.cs           # MODIFY: Validate new fields (transcription mode, model, VoiceLive voices)
│   └── CallService.cs              # MODIFY: Pass VoiceApiMode to handler, reconnection logic
├── Controllers/
│   └── SettingsController.cs        # MODIFY: Expose VoiceLive config status
├── Hubs/
│   └── TranscriptHub.cs             # EXISTING: No changes (reused for VoiceLive transcripts)
├── appsettings.json                 # MODIFY: Add VoiceLive config section
└── Program.cs                       # MODIFY: Register VoiceLiveService, configure VoiceLive settings

ContactCenter-APP/
├── Pages/
│   └── Index.cshtml                 # MODIFY: Enable VoiceLive radio, add model/transcription dropdowns
└── wwwroot/js/
    └── site.js                      # MODIFY: Voice list swap, model dropdown, transcription toggle, reconnect UI

CallCenterPOC-API.Tests/
├── Unit/
│   ├── VoiceLiveVoiceValidationTests.cs   # NEW: Voice catalog, locale grouping
│   └── SettingsValidationTests.cs          # NEW: TranscriptionMode, VoiceLiveModel validation
└── Contract/
    ├── SettingsControllerContractTests.cs  # MODIFY: Test new VoiceLive settings fields
    └── HealthCheckContractTests.cs         # MODIFY: Test VoiceLive config status
```

**Structure Decision**: Follows existing web application layout (API backend + Razor Pages frontend + Tests). No new projects added. VoiceLive service mirrors the existing `AzureOpenAIService` pattern. Dispatch logic in `AcsMediaStreamingHandler` — consistent with POC-first simplicity (Constitution I).

## Complexity Tracking

> No constitution violations. No justification needed.
