# Feature Specification: Azure AI VoiceLive Integration

**Feature Branch**: `002-voicelive-integration`  
**Created**: 2026-03-05  
**Status**: Draft  
**Input**: User description: "we need to add support for Azure AI VoiceLive service, can you check it on the feasibility of that. (there is an option on the setting menu we left as coming soon). this means adding all the new VoiceLive voice (Dragon etc.)"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Switch to VoiceLive Engine (Priority: P1)

As an operator, I want to switch the voice API engine from "ChatGPT Realtime" to "Voice Live" in the settings overlay so that outbound calls use Azure AI VoiceLive for speech synthesis and conversation.

**Why this priority**: This is the core value proposition — enabling the VoiceLive engine as an alternative to OpenAI Realtime. Without this, no other VoiceLive feature can function.

**Independent Test**: Open settings, select "Voice Live" radio button (currently disabled/coming soon), save settings, initiate a call, and verify the call uses VoiceLive for audio processing.

**Acceptance Scenarios**:

1. **Given** the settings overlay is open, **When** the operator selects "Voice Live", **Then** the radio button becomes active and the selection is persisted via the settings API.
2. **Given** VoiceLive is selected in settings, **When** the operator initiates a new call, **Then** the system connects to the Azure AI VoiceLive WebSocket endpoint instead of the OpenAI Realtime endpoint.
3. **Given** VoiceLive is active and a call is in progress, **When** the AI agent responds, **Then** audio is received from VoiceLive and played to the callee via ACS media streaming.
4. **Given** the operator switches back to "ChatGPT Realtime", **When** the next call is initiated, **Then** the system uses the OpenAI Realtime endpoint as before.
5. **Given** the VoiceLive WebSocket connection fails during session creation, **When** the operator initiates a call, **Then** the system logs the error and notifies the operator that the call could not start.

---

### User Story 2 - Select Dragon HD Voices (Priority: P1)

As an operator, I want to choose from the available Dragon HD voices when VoiceLive is selected so that the AI agent's voice matches the desired persona for outbound calls.

**Why this priority**: Voice selection is essential for the VoiceLive experience — Dragon HD voices are the primary differentiator from OpenAI Realtime voices.

**Independent Test**: Select VoiceLive mode, open the voice dropdown, choose a Dragon HD voice (e.g., "Ava"), start a call, and verify the AI speaks with the selected Dragon HD voice.

**Acceptance Scenarios**:

1. **Given** VoiceLive is selected in settings, **When** the operator opens the AI Voice dropdown, **Then** the dropdown lists the available Dragon HD voices (not the OpenAI voices).
2. **Given** the operator selects "Ava (Dragon HD)" from the voice list, **When** they save settings, **Then** the selected VoiceLive voice is persisted and used for subsequent calls.
3. **Given** the operator switches from VoiceLive to ChatGPT Realtime, **When** the voice dropdown updates, **Then** it shows the OpenAI voices (Alloy, Echo, etc.) instead of Dragon HD voices.
4. **Given** a previously saved VoiceLive voice that is no longer available, **When** settings are loaded, **Then** the system defaults to the first available Dragon HD voice.

---

### User Story 3 - Transcript & Sentiment Continue Working (Priority: P2)

As an operator, I want real-time transcription, sentiment analysis, and call summaries to continue working when using VoiceLive so that no existing functionality is lost.

**Why this priority**: Maintaining feature parity ensures the VoiceLive mode is a full replacement, not a downgraded experience.

**Independent Test**: Start a call with VoiceLive active, verify transcript entries appear in the live panel, verify sentiment badges update, and verify call summary is generated after the call ends.

**Acceptance Scenarios**:

1. **Given** a VoiceLive call is in progress, **When** the AI speaks or the callee speaks, **Then** transcript entries appear in the live transcript panel.
2. **Given** a VoiceLive call has ended, **When** the system processes the transcript, **Then** sentiment analysis, emotion analysis, and call summary are generated as with ChatGPT Realtime calls.
3. **Given** a VoiceLive call is in progress, **When** the call timer reaches the max call time setting, **Then** the call is automatically ended just as with ChatGPT Realtime calls.

---

### User Story 4 - VoiceLive Configuration in App Settings (Priority: P2)

As a system administrator, I want to configure the VoiceLive resource endpoint and credentials in application settings so that the system can authenticate with the Azure AI VoiceLive service.

**Why this priority**: Without configuration, the VoiceLive service cannot be reached. This is a deployment prerequisite.

**Independent Test**: Set the VoiceLive endpoint and credentials in app settings, start the application, and verify VoiceLive connectivity on the health check.

**Acceptance Scenarios**:

1. **Given** the VoiceLive endpoint and credentials are configured in application settings or environment variables, **When** the application starts, **Then** the VoiceLive client authenticates successfully.
2. **Given** the VoiceLive configuration is missing, **When** the operator selects VoiceLive mode and tries to initiate a call, **Then** the system returns a clear error indicating VoiceLive is not configured.
3. **Given** the VoiceLive endpoint uses Managed Identity (Entra ID), **When** the application runs in Azure, **Then** authentication works without API keys.

---

### Edge Cases

- What happens when VoiceLive is selected but the Azure resource is in a region that doesn't support Dragon HD voices? The static voice catalog includes only voices known to be supported in documented regions (see research.md §4). If a runtime voice selection error occurs, the system MUST log a warning and notify the operator. Proactive regional discovery is out of scope for this POC.
- How does the system handle VoiceLive WebSocket disconnections mid-call? The system attempts up to 3 automatic reconnects with exponential backoff. During reconnection, a clear status indicator (e.g., "Reconnecting… attempt 2/3") is shown to the operator with an option to manually end the call. If all 3 attempts fail, the call is gracefully ended with an error notification. **Note**: After a successful reconnection, the AI has no memory of the prior conversation — the session restarts fresh. The transcript history is preserved in the UI.
- What happens when the operator switches voice API mode while a call is in progress? The change should only take effect for subsequent calls, not the active one.
- What happens if the VoiceLive session exceeds the configured max call time? The same auto-hang-up logic MUST apply — VoiceLive calls respect `maxCallTimeMinutes` identically to ChatGPT Realtime calls.
- How does the system handle VoiceLive rate limits or quota exhaustion? The system MUST surface a clear error to the operator (see FR-024).

## Requirements *(mandatory)*

### Out of Scope

- **Custom voices and custom avatars**: VoiceLive supports custom voice creation, but this is excluded from the initial integration.
- **Inbound call support**: This feature covers outbound calls only; VoiceLive for inbound call handling is a separate feature.
- **VoiceLive-specific billing/usage tracking**: Per-call cost tracking or usage dashboards for VoiceLive are not included.

### Functional Requirements

- **FR-001**: The settings overlay MUST enable the "Voice Live" radio button (currently disabled with "Coming Soon" badge), allowing operators to select it as the active voice API engine.
- **FR-002**: When "Voice Live" is selected, the system MUST connect to the Azure AI VoiceLive WebSocket endpoint for new calls instead of the OpenAI Realtime endpoint.
- **FR-003**: The system MUST support bidirectional audio streaming between ACS Call Automation media streams and the VoiceLive WebSocket session.
- **FR-004**: When "Voice Live" is selected, the AI Voice dropdown MUST display the available Dragon HD voices instead of the OpenAI voices. The dropdown content MUST change dynamically based on the selected voice API mode.
- **FR-005**: The system MUST support at minimum the following en-US Dragon HD voices: Ava, Andrew, Adam, Brian, Davis, Emma, Jenny, Nova, Aria, Alloy, Phoebe, Steffan.
- **FR-006**: The selected VoiceLive voice MUST be persisted via the existing `PUT /api/Settings` endpoint and applied when creating new VoiceLive sessions.
- **FR-007**: The system MUST use the same system prompt (campaign prompt or default) for VoiceLive sessions as for ChatGPT Realtime sessions.
- **FR-008**: Real-time transcription MUST continue to work with VoiceLive calls, producing transcript entries that appear in the live transcript panel. The operator MUST be able to choose the transcription source via a setting: either (a) VoiceLive's built-in transcript events from the WebSocket session, or (b) a separate Azure Speech STT pipeline running alongside VoiceLive. *(See FR-016 for the UI control.)*
- **FR-009**: Post-call analysis (sentiment, emotion, call summary) MUST function identically for VoiceLive calls as for ChatGPT Realtime calls, regardless of the selected transcription source.
- **FR-016**: The settings overlay MUST include a "Transcription Source" dropdown (visible only when VoiceLive is selected) with options: "Built-in (VoiceLive)" and "Separate STT Pipeline". The default MUST be "Built-in (VoiceLive)". *(UI manifestation of FR-008.)*
- **FR-017**: When "Separate STT Pipeline" is selected, the system MUST use Azure Speech SDK speech-to-text on the call audio independently of VoiceLive, producing transcript entries in the same format as the built-in option.
- **FR-010**: The system MUST support authentication to the VoiceLive service via Managed Identity (DefaultAzureCredential) and optionally via API key.
- **FR-011**: The application configuration MUST include a new section for VoiceLive settings (endpoint URI and optional deployment/model name).
- **FR-012**: The health check endpoint MUST indicate VoiceLive configuration status (configured or not configured) without blocking the overall health check.
- **FR-013**: When VoiceLive is not configured, the "Voice Live" radio button MUST remain disabled with a "Not Configured" badge instead of "Coming Soon".
- **FR-014**: The voice API mode switch MUST only affect new calls. Calls already in progress MUST continue using the engine they started with.
- **FR-015**: The system MUST log which voice API engine is used for each call, including the specific voice selected.
- **FR-018**: When the VoiceLive WebSocket disconnects mid-call, the system MUST attempt up to 3 automatic reconnections with exponential backoff (e.g., 1s, 2s, 4s delays). If all 3 attempts fail, the system MUST gracefully end the call and notify the operator with an error message.
- **FR-019**: During VoiceLive reconnection attempts, the UI MUST display a clear call status indicator (e.g., "Reconnecting… attempt 2/3") so the operator knows the current state of the call.
- **FR-020**: During VoiceLive reconnection attempts, the operator MUST have the ability to manually end the call at any time via the existing hang-up control, without waiting for reconnection to complete or fail.
- **FR-021**: When "Voice Live" is selected, the settings overlay MUST display an "AI Model" dropdown listing the available VoiceLive-supported models (gpt-realtime, gpt-4o, gpt-4.1, gpt-5, phi-4-mini). The selected model MUST be persisted via the existing `PUT /api/Settings` endpoint.
- **FR-022**: The selected VoiceLive model MUST be applied when creating new VoiceLive sessions. The default model MUST be gpt-4o if no selection has been made.
- **FR-023**: The system MUST support multi-locale Dragon HD voices (not limited to en-US). The voice dropdown MUST display voices grouped or filterable by locale when VoiceLive is selected.
- **FR-024**: When VoiceLive returns a rate-limit or quota-exhaustion error during session creation or mid-call, the system MUST surface a clear error notification to the operator (e.g., "VoiceLive quota exceeded — please try again later") and MUST NOT silently drop the call.

### Key Entities

- **VoiceLiveSession**: Represents an active WebSocket connection to the Azure AI VoiceLive service. Analogous to the current OpenAI Realtime conversation session. Configured with a Dragon HD voice, model, system prompt, and turn detection settings.
- **VoiceLiveVoice**: Represents a Dragon HD voice identified by its full Azure Speech voice name (e.g., `en-US-Ava:DragonHDLatestNeural`). Has a display-friendly name (e.g., "Ava") and gender metadata.
- **VoiceApiMode**: Setting with values `ChatGPT` and `VoiceLive` that determines which voice engine is used for outbound calls. Already exists in `OperatorSettings`.
- **TranscriptionMode**: Setting with values `BuiltIn` and `SeparateSTT` that determines how transcription is produced during VoiceLive calls. `BuiltIn` uses VoiceLive's native transcript events; `SeparateSTT` uses an independent Azure Speech STT pipeline. Only applicable when VoiceApiMode is `VoiceLive`.
- **VoiceLiveModel**: Setting that specifies which LLM model to use for VoiceLive sessions (e.g., `gpt-4o`, `gpt-4.1`, `gpt-5`, `phi-4`). Operator-selectable in the settings overlay when VoiceLive is active. Defaults to `gpt-4o`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Operators can switch between ChatGPT Realtime and VoiceLive and save settings with end-to-end UI response in under 5 seconds on a standard connection.
- **SC-002**: A VoiceLive call completes successfully end-to-end (dial, AI conversation, hang up) with a success rate >= 95% in a controlled test session, comparable to ChatGPT Realtime calls tested under identical conditions.
- **SC-003**: At least 12 Dragon HD en-US voices are selectable from the settings dropdown when VoiceLive is active.
- **SC-004**: Transcript, sentiment, emotion, and call summary are generated for VoiceLive calls and produce non-empty, structurally equivalent results compared to ChatGPT Realtime calls, using either the built-in or separate STT transcription source as configured by the operator.
- **SC-005**: Switching voice API mode does not interrupt any call currently in progress.
- **SC-006**: The system gracefully handles VoiceLive configuration absence — operators see "Not Configured" and cannot initiate calls with an unconfigured engine.
- **SC-007**: VoiceLive calls use server-side echo cancellation, deep noise suppression, and Azure Semantic VAD, which are expected to reduce echo and cross-talk in phone conversations compared to ChatGPT Realtime calls.
- **SC-008**: Operators can select an AI model from the VoiceLive model dropdown and the selected model is used for new calls.

## Assumptions

- The Azure AI VoiceLive service (`Azure.AI.VoiceLive` NuGet 1.0.0 GA) is available in the deployment region (Southeast Asia). The implementation targets GA 1.0.0 for production stability; beta 1.1.0-beta.3 features should be evaluated for future upgrade (see Dependencies).
- VoiceLive uses a WebSocket event protocol compatible with the Azure OpenAI Realtime event format, making the bridging architecture similar to the existing media streaming handler pattern.
- Dragon HD voices follow the naming convention `{locale}-{Name}:DragonHDLatestNeural` (e.g., `en-US-Ava:DragonHDLatestNeural`).
- Authentication uses `DefaultAzureCredential` (same as the existing OpenAI integration), requiring the app's Managed Identity to have the appropriate role on the Speech/Foundry resource (see FR-010).
- The existing ACS Call Automation audio streaming (PCM 16-bit, 16kHz) is compatible with VoiceLive's audio input requirements.
- No additional Azure resource provisioning is required beyond creating a Microsoft Foundry or Azure Speech resource and assigning permissions.
- The en-US locale is the primary locale; multi-locale Dragon HD voices are also in scope and the voice dropdown will support multiple locales.

## Clarifications

### Session 2026-03-05

- Q: How should VoiceLive calls produce transcription — built-in events, separate STT, or ACS transcription? → A: Support both VoiceLive built-in transcript events and a separate Azure Speech STT pipeline, selectable by the operator in settings for flexibility.
- Q: What should happen when VoiceLive disconnects mid-call? → A: Attempt up to 3 reconnects with exponential backoff; show clear reconnection status to the operator (e.g., "Reconnecting… attempt 2/3"); allow the operator to manually end the call at any time during reconnection.
- Q: Should operators be able to select the LLM model for VoiceLive calls? → A: Yes, expose a model dropdown in the operator settings UI (GPT-5, GPT-4.1, GPT-4o, Phi-4) so operators can choose the model per their needs.
- Q: What should be explicitly out of scope? → A: Custom voices/avatars, inbound call support, and VoiceLive billing/usage tracking are out of scope. Multi-locale voices are IN scope.
- Q: Which Azure.AI.VoiceLive NuGet version to target? → A: Start with GA 1.0.0 for stability; document beta 1.1.0-beta.3 features to evaluate for future upgrade.

### Terminology Convention

- **Code/API values**: `VoiceLive` (no space) — used for `VoiceApiMode` enum value, JSON field values, and code identifiers.
- **UI labels**: "Voice Live" (with space) — used for user-facing display text in the settings overlay.

## Dependencies

- **Azure.AI.VoiceLive** NuGet package — target **1.0.0 GA** for production stability. Beta-only features in 1.1.0-beta.3 to evaluate for future upgrade include: enhanced transcript event metadata, additional turn detection modes, and any new voice configuration options added post-GA.
- An Azure AI Speech or Microsoft Foundry resource with VoiceLive enabled
- Managed Identity role assignment for the app service to access the VoiceLive resource
- Existing ACS Call Automation media streaming infrastructure (already in place)

## Feasibility Assessment

### Verdict: HIGH feasibility

**Architecture alignment**: The current codebase already has a `VoiceApiMode` field in `OperatorSettings` (values: `"ChatGPT"` or `"VoiceLive"`), a disabled "Voice Live" radio button in the UI with a "Coming Soon" badge, and a media streaming handler that bridges ACS audio to a WebSocket-based AI service. VoiceLive uses the same WebSocket + PCM audio pattern, making it architecturally compatible.

**SDK maturity**: The `Azure.AI.VoiceLive` NuGet package is at GA 1.0.0 with a beta 1.1.0-beta.3 available. The API follows familiar Azure SDK patterns (`VoiceLiveClient`, `VoiceLiveSession`, `VoiceLiveSessionOptions`).

**Key advantages of VoiceLive over current approach**:

| Capability | ChatGPT Realtime (current) | VoiceLive |
|------------|---------------------------|-----------|
| Voices | 6 OpenAI voices (Alloy, Echo, etc.) | 600+ Azure voices including 11+ Dragon HD |
| Echo cancellation | Not included | Server-side echo cancellation |
| Turn detection | Basic VAD | Azure Semantic VAD with filler word removal |
| Noise suppression | Not included | Built-in deep noise suppression |
| Models | GPT-4o-realtime only | GPT-5, GPT-4.1, GPT-4o, Phi-4, and more |
| Custom voices | Not supported | Custom voice and custom avatar |
| Audio quality for phone calls | Acceptable | Superior (echo cancellation + noise suppression) |
| Model deployment | Must deploy model in Azure OpenAI | Fully managed, no deployment needed |

**Effort estimate**: Medium. The core bridging logic is similar to the existing OpenAI handler. Main work involves creating a VoiceLive service class, updating the handler to dispatch based on `VoiceApiMode`, and expanding the voice dropdown to be mode-aware.

**Risk**: Low. The WebSocket event protocol is documented as compatible with Azure OpenAI Realtime events, and the existing PCM 16-bit audio format is supported.
