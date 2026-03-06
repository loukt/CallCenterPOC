# Research: Azure AI VoiceLive Integration

**Feature**: 002-voicelive-integration | **Date**: 2026-03-05

## 1. VoiceLive SDK API Surface

### Decision: Use `Azure.AI.VoiceLive` NuGet 1.0.0 GA (API version `2025-10-01`)
### Rationale: GA stability for production; beta 1.1.0-beta.2 adds agent mode and interim responses not needed in Phase 1.
### Alternatives considered: Beta 1.1.0-beta.2 offers agent mode (`AgentSessionConfig`) and preview API version `2026-01-01-preview`, but introduces pre-release risk without relevant benefits.

**Key classes**: `VoiceLiveClient`, `VoiceLiveSession`, `VoiceLiveSessionOptions`, `VoiceLiveClientOptions`

**Session lifecycle**:
1. Create `VoiceLiveClient` with endpoint + credential
2. `await client.StartSessionAsync("gpt-realtime")` → returns `VoiceLiveSession`
3. `await session.ConfigureSessionAsync(options)` → voice, VAD, audio format, instructions
4. `await session.SendInputAudioAsync(bytes)` — stream audio in
5. `await foreach (var event in session.GetUpdatesAsync(ct))` — process events
6. `session.Dispose()` — clean up

**Authentication**: `DefaultAzureCredential` (Managed Identity) or `AzureKeyCredential`. Required RBAC roles: **Cognitive Services User** + **Azure AI User**.

## 2. WebSocket Protocol Compatibility

### Decision: Direct bridge from ACS WebSocket to VoiceLive via `VoiceLiveSession` (same pattern as existing `AzureOpenAIService`)
### Rationale: VoiceLive uses the **same event protocol** as Azure OpenAI Realtime API with additive features. The existing `AcsMediaStreamingHandler` → `AzureOpenAIService` pattern maps 1:1 to `AcsMediaStreamingHandler` → `VoiceLiveService`.
### Alternatives considered: Raw WebSocket client (lower level, more work, no benefit); ACS Transcription feature (doesn't provide AI audio I/O).

**Event mapping** (existing OpenAI Realtime → VoiceLive equivalent):

| Current OpenAI Event (C#) | VoiceLive Event (C#) | Semantic |
|---------------------------|---------------------|----------|
| `ConversationInputSpeechStartedUpdate` | `SessionUpdateInputAudioBufferSpeechStarted` | User barge-in |
| `ConversationItemStreamingPartDeltaUpdate` (audio) | `SessionUpdateResponseAudioDelta` | AI audio chunk |
| `ConversationItemStreamingAudioTranscriptionFinishedUpdate` | `SessionUpdateResponseAudioTranscriptDelta` / done | AI transcript |
| `ConversationInputTranscriptionFinishedUpdate` | `SessionUpdateConversationItemInputAudioTranscriptionCompleted` | User transcript |
| `ConversationErrorUpdate` | `SessionUpdateError` | Error |

## 3. Audio Format Compatibility

### Decision: Use PCM16/16kHz for both input and output — native ACS format, no resampling needed
### Rationale: ACS Call Automation media streaming sends PCM 16-bit, 16kHz mono. VoiceLive supports this directly via `InputAudioFormat.Pcm16` + `input_audio_sampling_rate: 16000` and `OutputAudioFormat: "pcm16_16000hz"`. No resampling overhead.
### Alternatives considered: Upsample to 24kHz for potentially better quality, then downsample output — adds latency and complexity for marginal benefit in phone audio.

**Configuration**:
```csharp
options.InputAudioFormat = InputAudioFormat.Pcm16;   // 16-bit PCM
// input_audio_sampling_rate = 16000 (set via protocol)
options.OutputAudioFormat = OutputAudioFormat.Pcm16;  // Request pcm16_16000hz for ACS compat
```

**Note**: ACS `MediaStreamingOptions` currently uses `AudioFormat = Pcm24KMono` — this is PCM 16-bit at 24kHz. Need to verify: if ACS sends 24kHz, VoiceLive's default 24kHz input rate works without changes. Output should then use `Pcm16` (24kHz default). This may simplify the setup even further.

## 4. Dragon HD Voice Catalog

### Decision: Maintain a static voice catalog in code, grouped by locale, with a curated set of GA + key Preview voices
### Rationale: The voice list is relatively stable and documented. A static catalog avoids runtime discovery complexity. Can be updated with SDK upgrades.
### Alternatives considered: Runtime voice list API (not available in GA SDK); hardcode only en-US (conflicts with FR-023 multi-locale requirement).

**en-US Dragon HD voices (18 known)**:

| Voice | Full Name | Gender | Status |
|-------|-----------|--------|--------|
| Adam | `en-US-Adam:DragonHDLatestNeural` | Male | GA |
| Alloy | `en-US-Alloy:DragonHDLatestNeural` | Male | Preview |
| Andrew | `en-US-Andrew:DragonHDLatestNeural` | Male | GA |
| Andrew2 | `en-US-Andrew2:DragonHDLatestNeural` | Male | GA |
| Aria | `en-US-Aria:DragonHDLatestNeural` | Female | Preview |
| Ava | `en-US-Ava:DragonHDLatestNeural` | Female | GA |
| Brian | `en-US-Brian:DragonHDLatestNeural` | Male | GA |
| Davis | `en-US-Davis:DragonHDLatestNeural` | Male | GA |
| Emma | `en-US-Emma:DragonHDLatestNeural` | Female | GA |
| Emma2 | `en-US-Emma2:DragonHDLatestNeural` | Female | GA |
| Jenny | `en-US-Jenny:DragonHDLatestNeural` | Female | Preview |
| Nova | `en-US-Nova:DragonHDLatestNeural` | Female | Preview |
| Phoebe | `en-US-Phoebe:DragonHDLatestNeural` | Female | Preview |
| Serena | `en-US-Serena:DragonHDLatestNeural` | Female | Preview |
| Steffan | `en-US-Steffan:DragonHDLatestNeural` | Male | GA |

**Multi-locale sample (GA voices)**:

| Locale | Voices |
|--------|--------|
| de-DE | Florian (M), Seraphina (F) |
| es-ES | Tristan (M), Ximena (F) |
| fr-FR | Remy (M), Vivienne (F) |
| ja-JP | Masaru (M), Nanami (F) |
| zh-CN | Xiaochen (F), Yunfan (M) |

**Naming convention**: `{locale}-{PersonaName}:DragonHDLatestNeural`

**Supported regions**: southeastasia, centralindia, swedencentral, westeurope, eastus, eastus2, westus2

## 5. Transcription Events

### Decision: VoiceLive provides built-in transcript events matching the existing OpenAI pattern. The "Separate STT Pipeline" option will use Azure Speech SDK `SpeechRecognizer` on the same audio stream.
### Rationale: Built-in transcripts are the simplest option and align with the existing pattern. The separate STT option provides flexibility when users want different transcription models or when troubleshooting.
### Alternatives considered: ACS Call Automation built-in transcription (would bypass the AI pipeline entirely, not suitable).

**Built-in transcript events**:
- **User speech**: `conversation.item.input_audio_transcription.completed` → C# `SessionUpdateConversationItemInputAudioTranscriptionCompleted.Transcript`
- **AI speech**: `response.audio_transcript.delta` / `response.audio_transcript.done` → C# `SessionUpdateResponseAudioTranscriptDelta.Delta`

**Transcription model for non-realtime models (gpt-4o, gpt-4.1, gpt-5, phi-4)**:
- Must use `azure-speech` for input transcription (not Whisper)
- Realtime models (gpt-realtime, gpt-realtime-mini) support `whisper-1`, `gpt-4o-transcribe`

## 6. Model Support

### Decision: Expose curated model list: `gpt-realtime`, `gpt-4o`, `gpt-4.1`, `gpt-5`, `phi-4-mini` (5 models covering all price tiers)
### Rationale: Full list of 12+ models would overwhelm the dropdown. Curated set covers Pro, Basic, and Lite tiers. Operator can choose based on quality/cost tradeoff.
### Alternatives considered: Expose all 12 models (UI clutter); hardcode single model (conflicts with FR-021 operator choice requirement).

**Curated model list**:

| Model ID | Display Name | Tier | Notes |
|----------|-------------|------|-------|
| `gpt-realtime` | GPT Realtime | Pro | Native multimodal, best quality |
| `gpt-4o` | GPT-4o | Pro | Cascaded (Azure STT + LLM + Azure TTS) |
| `gpt-4.1` | GPT-4.1 | Pro | Latest reasoning model |
| `gpt-5` | GPT-5 | Pro | Most capable |
| `phi-4-mini` | Phi-4 Mini | Lite | Cost-optimized |

**Model specified** by passing model ID to `client.StartSessionAsync("gpt-4o")`.

**No deployment needed** — VoiceLive is fully managed.

## 7. Echo Cancellation & VAD

### Decision: Enable server-side echo cancellation + Azure Deep Noise Suppression + Azure Semantic VAD by default for all VoiceLive calls
### Rationale: These are key VoiceLive advantages over ChatGPT Realtime, especially for phone calls where ACS may relay audio that includes the AI's own voice.
### Alternatives considered: Make configurable per-operator (over-engineering for POC; Constitution I).

**Configuration**:
```csharp
options.InputAudioEchoCancellation = new AudioEchoCancellation();
options.InputAudioNoiseReduction = new AudioNoiseReduction(AudioNoiseReductionType.AzureDeepNoiseSuppression);
options.TurnDetection = new AzureSemanticVadTurnDetection();
// remove_filler_words = true by default in Azure Semantic VAD
```

**Constraint**: Client must play response audio within 2 seconds of receiving it for echo cancellation to work correctly. The ACS media streaming bridge forwards audio immediately, so this should be satisfied.

## 8. Reconnection Strategy

### Decision: Manual reconnection with up to 3 attempts and exponential backoff (1s, 2s, 4s). Application-level implementation.
### Rationale: No SDK built-in reconnection. FR-018/019/020 specify the behavior. The existing `AcsMediaStreamingHandler` already handles WebSocket drops gracefully — same pattern extended for VoiceLive.
### Alternatives considered: SDK reconnection (not available); infinite retry (bad UX, could accumulate costs).

**Implementation approach**:
1. On `SessionUpdateError` or WebSocket close, start reconnection timer
2. Create new `VoiceLiveClient` + `StartSessionAsync` + `ConfigureSessionAsync` with same options
3. If reconnect succeeds, resume audio forwarding (note: conversation context is lost — new session starts fresh)
4. Signal reconnection status via SignalR `CallStatusChanged` with new status values: `Reconnecting`, `ReconnectFailed`
5. Operator can hang up at any time during reconnection

**Important**: A new VoiceLive session does NOT retain conversation context from the previous session. The reconnected session starts fresh. The transcript history is preserved client-side but the AI has no memory of the prior conversation after reconnect.

## 9. Implementation Architecture

### VoiceLiveService Pattern

The new `VoiceLiveService` mirrors `AzureOpenAIService`, implementing the same logical flow:

```
ACS WebSocket → AcsMediaStreamingHandler → VoiceLiveService → VoiceLive WebSocket
                                         ↕ SignalR (transcripts, sentiment, emotion, status)
```

**Dispatch point**: `AcsMediaStreamingHandler.ProcessWebSocketAsync()` checks `VoiceApiMode` and creates either `AzureOpenAIService` or `VoiceLiveService`.

### Settings Expansion

`OperatorSettings` gains three new fields:
- `TranscriptionMode`: `"BuiltIn"` | `"SeparateSTT"` (default: `"BuiltIn"`)
- `VoiceLiveModel`: `"gpt-realtime"` | `"gpt-4o"` | `"gpt-4.1"` | `"gpt-5"` | `"phi-4-mini"` (default: `"gpt-4o"`)
- `SelectedVoiceLiveVoice`: full voice name (default: `"en-US-Ava:DragonHDLatestNeural"`)

### App Configuration

New `appsettings.json` section:
```json
{
  "VoiceLive": {
    "EndpointUri": "",
    "Key": ""
  }
}
```

Managed Identity is default (no key needed). Key is optional for local development.

## 10. Beta Features to Evaluate for Future Upgrade

| Feature | Available In | Benefit |
|---------|-------------|---------|
| Agent mode (`AgentSessionConfig`) | 1.1.0-beta.2 | Pre-configured agent personas |
| Interim response | 1.1.0-beta.2, API `2026-01-01-preview` | Better turn-taking |
| MCP tool calling | API `2025-10-01` (GA) | External tool integration |
| DragonHDOmni voices (700+) | GA | Broader voice selection |
| Multi-talker voices | Preview | Multi-speaker scenarios |
