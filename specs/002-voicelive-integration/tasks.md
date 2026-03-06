# Tasks: Azure AI VoiceLive Integration

**Input**: Design documents from `/specs/002-voicelive-integration/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included in Phase 7 (Polish) — targeted unit and contract tests per plan.md.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing. US1 is the MVP milestone.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3, US4)
- Exact file paths included in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Install VoiceLive dependency, add configuration binding, prepare project structure

- [ ] T001 Install Azure.AI.VoiceLive 1.0.0 NuGet package in ContactCenter-API/ContactCenter-API.csproj
- [ ] T002 [P] Add VoiceLive configuration section to ContactCenter-API/appsettings.json
- [ ] T003 [P] Create VoiceLiveConfig options class in ContactCenter-API/Models/VoiceLiveConfig.cs and bind from configuration in ContactCenter-API/Program.cs

**Details**:

- **T001**: `dotnet add ContactCenter-API/ContactCenter-API.csproj package Azure.AI.VoiceLive --version 1.0.0`
- **T002**: Add `"VoiceLive": { "EndpointUri": "", "Key": "" }` section per quickstart.md
- **T003**: Create `VoiceLiveConfig` class in **ContactCenter-API/Models/VoiceLiveConfig.cs** (see data-model.md) with `EndpointUri`, `Key`, `IsConfigured` property. Register in Program.cs with `builder.Services.Configure<VoiceLiveConfig>(builder.Configuration.GetSection("VoiceLive"))` and also `builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<VoiceLiveConfig>>().Value)` for direct injection.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core models, validation, and API changes that ALL user stories depend on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [ ] T004 [P] Create VoiceLiveVoices static catalog with Dragon HD voices grouped by locale in ContactCenter-API/Models/VoiceLiveVoices.cs
- [ ] T005 [P] Add TranscriptionMode, VoiceLiveModel, SelectedVoiceLiveVoice fields and ValidVoiceLiveModels/ValidTranscriptionModes static sets to ContactCenter-API/Models/OperatorSettings.cs
- [ ] T006 [P] Add VoiceApiMode, VoiceLiveModel, ReconnectAttempts fields and Reconnecting enum value to CallStatus in ContactCenter-API/Models/ActiveCall.cs
- [ ] T007 [P] Add VoiceApiMode, VoiceLiveModel, VoiceLiveVoice fields to ContactCenter-API/Models/CallRecord.cs
- [ ] T008 Update SettingsService validation to handle new VoiceLive fields (mode-conditional validation) in ContactCenter-API/Services/SettingsService.cs
- [ ] T009 Update SettingsController GET response to include voiceLiveConfigured, availableVoiceLiveVoices, availableVoiceLiveModels metadata in ContactCenter-API/Controllers/SettingsController.cs

**Details**:

- **T004**: Static `VoiceLiveVoices` class with `VoiceLiveVoiceInfo` record. 22+ voices across 6 locales (en-US, de-DE, es-ES, fr-FR, ja-JP, zh-CN). Expose `All`, `ByLocale`, `ValidNames`, `Locales` members. See data-model.md and research.md §4 for full catalog.
- **T005**: Three new fields with defaults (`"BuiltIn"`, `"gpt-4o"`, `"en-US-Ava:DragonHDLatestNeural"`). Two static HashSets for validation. Existing `VoiceApiMode` and `ValidVoices` fields remain unchanged. See data-model.md OperatorSettings entity.
- **T006**: `VoiceApiMode` string (frozen at call start per FR-014), nullable `VoiceLiveModel`, `ReconnectAttempts` int (default 0). Add `Reconnecting` to `CallStatus` enum. See data-model.md ActiveCall entity and state transitions.
- **T007**: Three new string fields for call history persistence. See data-model.md CallRecord entity.
- **T008**: When `VoiceApiMode == "VoiceLive"`: validate `SelectedVoiceLiveVoice` against `VoiceLiveVoices.ValidNames`, `VoiceLiveModel` against `ValidVoiceLiveModels`, `TranscriptionMode` against `ValidTranscriptionModes`. Default unknown values per data-model.md rules. When `VoiceApiMode == "ChatGPT"`: existing validation only.
- **T009**: GET response adds `voiceLiveConfigured` (from `VoiceLiveConfig.IsConfigured`), `availableVoiceLiveVoices` (from `VoiceLiveVoices.All`), `availableVoiceLiveModels` (from `OperatorSettings.ValidVoiceLiveModels`). See contracts/settings-api.yaml SettingsResponse schema.

**Checkpoint**: Foundation ready — user story implementation can now begin

---

## Phase 3: User Story 1 — Switch to VoiceLive Engine (Priority: P1) 🎯 MVP

**Goal**: Operators can select VoiceLive as the voice engine and make successful outbound calls using VoiceLive with reconnection support

**Independent Test**: Open settings → select "Voice Live" → select a model → save → initiate a call → verify audio flows through VoiceLive WebSocket → simulate disconnect → verify reconnection attempts with status indicator → verify operator can abort during reconnect

**Maps to**: FR-001, FR-002, FR-003, FR-007, FR-010, FR-013, FR-014, FR-015, FR-018, FR-019, FR-020, FR-021, FR-022, FR-024, SC-001, SC-002, SC-005, SC-007, SC-008

### Implementation for User Story 1

- [ ] T010 [US1] Create VoiceLiveService with session lifecycle (VoiceLiveClient, StartSessionAsync, ConfigureSessionAsync, audio send/receive loop, dispose) in ContactCenter-API/Services/VoiceLiveService.cs
- [ ] T011 [US1] Register VoiceLiveService in DI container in ContactCenter-API/Program.cs
- [ ] T012 [US1] Update acsMediaStreamingHandler to dispatch to VoiceLiveService or AzureOpenAIService based on VoiceApiMode in ContactCenter-API/Models/acsMediaStreamingHandler.cs
- [ ] T013 [US1] Update CallService to freeze VoiceApiMode/VoiceLiveModel at call start (FR-014) and pass to media streaming handler in ContactCenter-API/Services/CallService.cs
- [ ] T014 [US1] Implement reconnection logic in VoiceLiveService with exponential backoff (1s, 2s, 4s — max 3 attempts) in ContactCenter-API/Services/VoiceLiveService.cs
- [ ] T015 [US1] Emit Reconnecting and ReconnectFailed SignalR events via TranscriptHub during VoiceLive reconnection in ContactCenter-API/Services/VoiceLiveService.cs
- [ ] T016 [US1] Add VoiceLive engine and voice logging per call (FR-015) in ContactCenter-API/Services/CallService.cs
- [ ] T017 [P] [US1] Enable VoiceLive radio button, implement Not Configured vs enabled badge logic, and add AI Model dropdown (FR-001, FR-013, FR-021) in ContactCenter-APP/Pages/Index.cshtml
- [ ] T018 [US1] Update site.js to handle VoiceLive mode selection, model dropdown population/save/load, and toggle mode-specific UI sections in ContactCenter-APP/wwwroot/js/site.js
- [ ] T019 [US1] Update site.js to display reconnection status indicator ("Reconnecting… attempt N/3") and enable operator abort during reconnect (FR-019, FR-020) in ContactCenter-APP/wwwroot/js/site.js

**Details**:

- **T010**: Mirror `AzureOpenAIService` pattern (see research.md §9). Create `VoiceLiveClient` with endpoint + `DefaultAzureCredential` (or `AzureKeyCredential` if key provided). Session lifecycle: `StartSessionAsync(model)` → `ConfigureSessionAsync(options)` with voice, system prompt, echo cancellation, noise suppression, Azure Semantic VAD per research.md §7. Audio format: PCM16 matching ACS format (research.md §3). Event loop: `GetUpdatesAsync()` processing audio deltas, transcripts, errors. `SendInputAudioAsync()` for forwarding ACS audio. **Acceptance criterion**: Verify ACS audio format (Pcm24KMono = 24kHz) is compatible with VoiceLive input; if ACS sends 24kHz, configure VoiceLive for 24kHz input/output accordingly. If formats don’t match, this is a blocker requiring resampling or ACS format reconfiguration.
- **T011**: Register as transient/scoped service. Inject `VoiceLiveConfig`, `ILogger<VoiceLiveService>`, `IHubContext<TranscriptHub>`.
- **T012**: In `ProcessWebSocketAsync()`, check `VoiceApiMode` from the call's active settings. If `"VoiceLive"`: create and use `VoiceLiveService`. If `"ChatGPT"`: use existing `AzureOpenAIService`. No abstract factory — simple `if/switch` per Constitution I.
- **T013**: When creating `ActiveCall`, copy `VoiceApiMode` and `VoiceLiveModel` from current `OperatorSettings` and freeze them for the call's duration. Pass these to the media streaming handler creation.
- **T014**: On WebSocket close or `SessionUpdateError`, start reconnection timer. Create new session with same options. If success: resume audio forwarding (note: conversation context is lost — research.md §8). If all 3 attempts fail: end call gracefully. Also handle rate-limit and quota-exhaustion errors (`SessionUpdateError` with throttling codes): surface a clear notification to the operator via SignalR (FR-024) and do not retry.
- **T015**: Broadcast `CallStatusChanged` with status `"Reconnecting"` (message: "Reconnecting… attempt N/3") and `"ReconnectFailed"` (message: "All reconnection attempts failed"). See data-model.md SignalR Events.
- **T016**: Log `Information` with engine type, voice, and model when call starts. Log `Warning` on reconnection attempts.
- **T017**: Remove `disabled` attribute and "Coming Soon" badge from VoiceLive radio. Add conditional badge: show "Not Configured" (disabled) when `voiceLiveConfigured == false`, show enabled when `true`. Add AI Model `<select>` element below voice dropdown, visible only when VoiceLive is selected.
- **T018**: In `loadSettings()`: populate model dropdown from `availableVoiceLiveModels`, set selected from `voiceLiveModel`. In `saveSettings()`: include `voiceLiveModel` in PUT body. Toggle model dropdown visibility on mode change. Disable VoiceLive radio if `voiceLiveConfigured == false`.
- **T019**: Listen for `CallStatusChanged` SignalR event with `"Reconnecting"` status. Display overlay/banner with attempt count. Keep hang-up button active during reconnect. On `"ReconnectFailed"`: show error notification and reset call UI.

**Checkpoint**: VoiceLive calls work end-to-end with model selection, reconnection, and operator abort. This is the MVP.

---

## Phase 4: User Story 2 — Select Dragon HD Voices (Priority: P1)

**Goal**: Operators can choose from Dragon HD voices (multi-locale) when VoiceLive is selected, with dynamic dropdown swapping between OpenAI and Dragon HD voices

**Independent Test**: Select VoiceLive mode → open voice dropdown → see Dragon HD voices grouped by locale → select "Ava (en-US)" → save → start call → verify AI speaks with Ava voice. Switch back to ChatGPT → verify OpenAI voices appear.

**Maps to**: FR-004, FR-005, FR-006, FR-023, SC-003

### Implementation for User Story 2

- [ ] T020 [US2] Update site.js voice dropdown to dynamically swap between OpenAI voices and Dragon HD voices based on selected voice API mode in ContactCenter-APP/wwwroot/js/site.js
- [ ] T021 [US2] Implement locale grouping in voice dropdown using optgroup elements for Dragon HD voices (FR-023) in ContactCenter-APP/wwwroot/js/site.js
- [ ] T022 [US2] Wire selectedVoiceLiveVoice to VoiceLiveService session creation (pass voice name to ConfigureSessionAsync options) in ContactCenter-API/Services/VoiceLiveService.cs
- [ ] T023 [US2] Implement voice fallback in SettingsService — default to first available Dragon HD voice when saved voice is unavailable in ContactCenter-API/Services/SettingsService.cs

**Details**:

- **T020**: On mode radio change: if VoiceLive → clear voice `<select>` and populate from `availableVoiceLiveVoices` (from GET /api/Settings response), using `fullName` as value and `displayName (locale)` as text. If ChatGPT → restore original OpenAI voice list. In `saveSettings()`: send `selectedVoiceLiveVoice` for VoiceLive mode, `selectedVoice` for ChatGPT mode.
- **T021**: Group voices by `locale` field using `<optgroup label="English (en-US)">` etc. Sort groups alphabetically, with en-US first. Within each group, sort by displayName.
- **T022**: In `ConfigureSessionAsync()` options, set the voice property to the `SelectedVoiceLiveVoice` value from settings (e.g., `"en-US-Ava:DragonHDLatestNeural"`).
- **T023**: In validation logic (T008 extended): if `SelectedVoiceLiveVoice` is not in `VoiceLiveVoices.ValidNames`, log warning and default to `"en-US-Ava:DragonHDLatestNeural"`. Return the corrected value in the response.

**Checkpoint**: Voice selection works for both modes. Dragon HD voices grouped by locale. US1 + US2 together form the complete P1 milestone.

---

## Phase 5: User Story 3 — Transcript & Sentiment Continue Working (Priority: P2)

**Goal**: Real-time transcription, sentiment analysis, and call summaries work identically for VoiceLive calls, with operator-selectable transcription source

**Independent Test**: Start VoiceLive call → verify transcript entries appear in live panel → end call → verify sentiment, emotion, and summary generated. Repeat with "Separate STT Pipeline" transcription source and verify same results.

**Maps to**: FR-008, FR-009, FR-016, FR-017, SC-004

### Implementation for User Story 3

- [ ] T024 [US3] Implement built-in VoiceLive transcript event handling (user speech + AI speech events) in ContactCenter-API/Services/VoiceLiveService.cs
- [ ] T025 [US3] Forward VoiceLive transcript events to SignalR TranscriptHub using existing transcript entry format in ContactCenter-API/Services/VoiceLiveService.cs
- [ ] T025a [US3] Install Microsoft.CognitiveServices.Speech NuGet package in ContactCenter-API/ContactCenter-API.csproj (required only if SeparateSTT is implemented)
- [ ] T026 [US3] Implement SeparateSTT pipeline using Azure Speech SDK SpeechRecognizer running alongside VoiceLive audio stream in ContactCenter-API/Services/VoiceLiveService.cs
- [ ] T027 [P] [US3] Add Transcription Source dropdown (visible only when VoiceLive is selected, options: Built-in / Separate STT) in ContactCenter-APP/Pages/Index.cshtml
- [ ] T028 [US3] Update site.js for transcription mode toggle visibility, save/load of transcriptionMode setting in ContactCenter-APP/wwwroot/js/site.js
- [ ] T029 [US3] Verify post-call analysis pipeline (sentiment, emotion, summary) receives VoiceLive transcripts correctly in ContactCenter-API/Services/CallService.cs

**Details**:

- **T024**: In the `GetUpdatesAsync()` event loop (T010), handle: `SessionUpdateConversationItemInputAudioTranscriptionCompleted` → user transcript; `SessionUpdateResponseAudioTranscriptDelta` / done → AI transcript. Extract `Transcript` / `Delta` text. See research.md §5 for event mapping. For non-realtime models (gpt-4o, gpt-4.1, gpt-5, phi-4-mini), transcription uses `azure-speech` input model.
- **T025**: Format transcript entries to match existing `AzureOpenAIService` output format. Send via `IHubContext<TranscriptHub>` using same method names (`ReceiveTranscription` or equivalent). This ensures the frontend transcript panel works without changes.
- **T026**: Prerequisite: T025a (Speech SDK NuGet). When `TranscriptionMode == "SeparateSTT"`: create `SpeechRecognizer` with the same Azure Speech resource. Feed the call audio (from ACS) into the recognizer alongside VoiceLive. Forward recognized text as transcript entries. When `TranscriptionMode == "BuiltIn"`: skip this pipeline (use T024/T025 instead). Note: This adds complexity — consider if the built-in transcription quality is sufficient for MVP.
- **T027**: Add `<select id="transcriptionSource">` with options `"BuiltIn"` ("Built-in (VoiceLive)") and `"SeparateSTT"` ("Separate STT Pipeline"). Wrap in a container `div` that is shown/hidden based on VoiceLive mode selection.
- **T028**: In `loadSettings()`: set `#transcriptionSource` value from `transcriptionMode`. In `saveSettings()`: include `transcriptionMode` in PUT body. Toggle visibility: show only when VoiceLive radio is selected.
- **T029**: Verify that the transcript accumulator in `CallService` (used for post-call OpenAI analysis) receives VoiceLive transcripts the same way as ChatGPT transcripts. The sentiment/emotion/summary call should work identically since it operates on the accumulated transcript text, not the source. Also verify that VoiceLive calls respect `maxCallTimeMinutes` auto-hangup identically to ChatGPT Realtime calls.

**Checkpoint**: Full transcript and analysis feature parity between VoiceLive and ChatGPT Realtime modes

---

## Phase 6: User Story 4 — VoiceLive Configuration (Priority: P2)

**Goal**: System administrators can configure VoiceLive endpoint, the health check shows VoiceLive status, and unconfigured state is clearly communicated

**Independent Test**: Deploy without VoiceLive config → verify health check shows `configured: false` → verify UI shows "Not Configured" badge → add endpoint config → restart → verify health check shows `configured: true` → verify VoiceLive radio is enabled

**Maps to**: FR-010, FR-011, FR-012, FR-013, SC-006

### Implementation for User Story 4

- [ ] T030 [US4] Update health check endpoint to include VoiceLive configuration status (configured flag + masked endpoint) in ContactCenter-API/Program.cs
- [ ] T031 [US4] Implement clear error handling when operator initiates VoiceLive call without configuration in ContactCenter-API/Services/CallService.cs
- [ ] T032 [US4] Add VoiceLive configuration validation on startup with warning log when endpoint is missing in ContactCenter-API/Program.cs

**Details**:

- **T030**: Extend the existing `/healthz` endpoint response. Add `voiceLive` object with `configured` (bool from `VoiceLiveConfig.IsConfigured`) and `endpoint` (masked, e.g., `"*.services.ai.azure.com"`). VoiceLive being unconfigured should NOT make the health check unhealthy — it's optional. See contracts/health-api.yaml.
- **T031**: In `CallService`, before creating a VoiceLive media handler: check `VoiceLiveConfig.IsConfigured`. If `false`, return error to operator (e.g., via SignalR notification: "VoiceLive is not configured. Please contact your administrator.") and do not proceed with the call.
- **T032**: On startup, if `VoiceLive:EndpointUri` is empty, log `Information`: "VoiceLive endpoint not configured — VoiceLive mode will be unavailable." No crash, no exception — just informational.

**Checkpoint**: VoiceLive configuration is observable via health check, absent config is handled gracefully

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Tests, cleanup, and validation across all user stories

- [ ] T033 [P] Create VoiceLiveVoiceValidationTests (voice catalog completeness, locale grouping, ValidNames consistency) in CallCenterPOC-API.Tests/Unit/VoiceLiveVoiceValidationTests.cs
- [ ] T034 [P] Create SettingsValidationTests (TranscriptionMode, VoiceLiveModel, SelectedVoiceLiveVoice validation and defaults) in CallCenterPOC-API.Tests/Unit/SettingsValidationTests.cs
- [ ] T035 [P] Update SettingsControllerContractTests for new VoiceLive fields in GET/PUT responses in CallCenterPOC-API.Tests/Contract/SettingsControllerContractTests.cs
- [ ] T036 [P] Update HealthCheckContractTests for VoiceLive configuration status in response in CallCenterPOC-API.Tests/Contract/HealthCheckContractTests.cs
- [ ] T037 Code cleanup, error handling review, and edge case hardening across all modified files
- [ ] T038 Run quickstart.md validation steps (NuGet installed, config present, RBAC documented, health check works) and verify SC-001 (settings mode switch round-trip < 5 seconds)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Foundational (Phase 2) — this is the MVP
- **US2 (Phase 4)**: Depends on Foundational (Phase 2). Can run in parallel with US1 (different files for UI voice dropdown vs service creation), but full testing requires US1's VoiceLiveService
- **US3 (Phase 5)**: Depends on US1 completion (needs working VoiceLiveService event loop)
- **US4 (Phase 6)**: Depends on Foundational (Phase 2). Can run in parallel with US1/US2 (health check + config validation are independent)
- **Polish (Phase 7)**: Depends on all user stories being complete

### User Story Dependencies

- **US1 (P1)**: Can start after Phase 2. No dependencies on other stories. **This is the MVP.**
- **US2 (P1)**: Can start after Phase 2. Voice dropdown UI work (T020-T021) can start in parallel with US1. Backend wiring (T022) depends on T010 (VoiceLiveService exists).
- **US3 (P2)**: Depends on US1 completion. Transcript events (T024-T025) extend VoiceLiveService created in T010. SeparateSTT (T026) is independent but needs the audio stream from T010.
- **US4 (P2)**: Can start after Phase 2. Health check (T030) and config validation (T031-T032) are independent of other stories.

### Within Each User Story

- Backend service tasks before frontend UI tasks (UI needs API to work)
- Core session lifecycle (T010) before reconnection (T014) before logging (T016)
- Voice dropdown swap (T020) before locale grouping (T021)
- Built-in transcripts (T024) before SeparateSTT (T026) — built-in is simpler path

### Parallel Opportunities

**Phase 2 — All foundational tasks marked [P] (T004-T007) can run in parallel** (different files):
```
T004: VoiceLiveVoices.cs    ─┐
T005: OperatorSettings.cs    ├─ All in parallel (4 different files)
T006: ActiveCall.cs          │
T007: CallRecord.cs         ─┘
Then: T008 (SettingsService) → T009 (SettingsController) — sequential, depend on T004-T007
```

**Phase 3 (US1) — Backend + Frontend split**:
```
T010 → T011 → T012 → T013  (backend: sequential — service → DI → dispatch → call service)
T017                        (frontend HTML: parallel with backend, different project)
T014 → T015                 (reconnection: after T010 — extends VoiceLiveService)
T016                        (logging: after T013 — extends CallService)
T018 → T019                 (frontend JS: after T017 HTML + T009 API response available)
```

**Phase 4 (US2) — Frontend can start with Phase 3**:
```
T020 → T021                 (frontend voice dropdown: can start once T009 provides voices)
T022                        (backend wiring: after T010 VoiceLiveService exists)
T023                        (fallback: after T008 validation exists)
```

**Phase 7 — All test tasks marked [P] can run in parallel**:
```
T033: VoiceLiveVoiceValidationTests  ─┐
T034: SettingsValidationTests         ├─ All in parallel (4 different files)
T035: SettingsControllerContractTests │
T036: HealthCheckContractTests       ─┘
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001-T003)
2. Complete Phase 2: Foundational (T004-T009)
3. Complete Phase 3: US1 — Switch to VoiceLive Engine (T010-T019)
4. **STOP and VALIDATE**: Test VoiceLive call end-to-end — operator switches to VoiceLive, selects model, initiates call, audio flows, reconnection works
5. Deploy/demo if ready — this proves VoiceLive integration works

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. **US1** → VoiceLive calls work → **Deploy/Demo (MVP!)**
3. **US2** → Dragon HD voice selection → Deploy/Demo (P1 complete)
4. **US3** → Full transcript + sentiment parity → Deploy/Demo
5. **US4** → Health check + config status → Deploy/Demo (feature complete)
6. **Polish** → Tests, cleanup, validation → Final release

### Key Risk: SeparateSTT Pipeline (T026)

The SeparateSTT pipeline (FR-017) adds significant complexity. If built-in VoiceLive transcription quality is sufficient, T026 can be deferred to a follow-up. The built-in path (T024-T025) should be validated first.

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks
- [Story] label maps task to specific user story for traceability
- All file paths are relative to repository root
- VoiceLive conversation context is NOT retained after reconnect (research.md §8) — this is a known limitation
- Audio format: PCM 16-bit — **MUST verify** ACS 24kHz vs VoiceLive compatibility during T010 (see T010 acceptance criterion and research.md §3 note)
- No abstract factories or plugin architecture — simple if/switch dispatch per Constitution I
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
