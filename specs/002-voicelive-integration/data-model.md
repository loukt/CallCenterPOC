# Data Model: Azure AI VoiceLive Integration

**Feature**: 002-voicelive-integration | **Date**: 2026-03-05

> **Terminology**: `VoiceLive` (no space) is used for code values, JSON fields, and entity names. "Voice Live" (with space) is the user-facing UI label.

## Entity Definitions

### OperatorSettings (MODIFY existing)

**File**: `ContactCenter-API/Models/OperatorSettings.cs`

| Field | Type | Default | Validation | Notes |
|-------|------|---------|------------|-------|
| `MaxCallTimeMinutes` | `double` | `2.0` | `[0.5, 30]` | Existing — no change |
| `VoiceApiMode` | `string` | `"ChatGPT"` | `"ChatGPT"` or `"VoiceLive"` | Existing — no change |
| `SelectedVoice` | `string` | `"alloy"` | Must be in `ValidVoices` | Existing — used when mode is ChatGPT |
| `TranscriptionMode` | `string` | `"BuiltIn"` | `"BuiltIn"` or `"SeparateSTT"` | **NEW** — only relevant when VoiceApiMode is VoiceLive |
| `VoiceLiveModel` | `string` | `"gpt-4o"` | Must be in `ValidVoiceLiveModels` | **NEW** — LLM model for VoiceLive sessions |
| `SelectedVoiceLiveVoice` | `string` | `"en-US-Ava:DragonHDLatestNeural"` | Must be in `VoiceLiveVoices.All` | **NEW** — voice for VoiceLive sessions |

**Validation rules**:
- When `VoiceApiMode == "ChatGPT"`: `SelectedVoice` validated against `ValidVoices` (existing behavior)
- When `VoiceApiMode == "VoiceLive"`: `SelectedVoiceLiveVoice` validated against `VoiceLiveVoices.All`; `VoiceLiveModel` validated against `ValidVoiceLiveModels`; `TranscriptionMode` validated against allowed values
- `TranscriptionMode` is ignored when `VoiceApiMode == "ChatGPT"`
- Unknown `VoiceLiveModel` defaults to `"gpt-4o"`
- Unknown `SelectedVoiceLiveVoice` defaults to `"en-US-Ava:DragonHDLatestNeural"`

**Static sets**:
```csharp
public static readonly HashSet<string> ValidVoiceLiveModels = new(StringComparer.OrdinalIgnoreCase)
{
    "gpt-realtime", "gpt-4o", "gpt-4.1", "gpt-5", "phi-4-mini"
};

public static readonly HashSet<string> ValidTranscriptionModes = new(StringComparer.OrdinalIgnoreCase)
{
    "BuiltIn", "SeparateSTT"
};
```

---

### ActiveCall (MODIFY existing)

**File**: `ContactCenter-API/Models/ActiveCall.cs`

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| All existing fields | — | — | No changes |
| `VoiceApiMode` | `string` | `"ChatGPT"` | **NEW** — records which engine this call uses (frozen at call start per FR-014) |
| `VoiceLiveModel` | `string?` | `null` | **NEW** — which LLM model (only when VoiceApiMode is VoiceLive) |
| `ReconnectAttempts` | `int` | `0` | **NEW** — tracks reconnection attempts for this call |

**State transitions** (updated `CallStatus` enum):

```
Initiating → Ringing → Connected → Disconnected
                     → Failed
                     → Reconnecting → Connected (reconnect success)
                                    → Disconnected (all retries exhausted)
                                    → Disconnected (operator manual hangup during reconnect)
```

New enum value: `Reconnecting` added to `CallStatus`.

---

### VoiceLiveVoices (NEW)

**File**: `ContactCenter-API/Models/VoiceLiveVoices.cs`

Static catalog of Dragon HD voices grouped by locale.

```csharp
public record VoiceLiveVoiceInfo(string FullName, string DisplayName, string Locale, string Gender);
```

| Property | Type | Description |
|----------|------|-------------|
| `FullName` | `string` | Full Azure voice name, e.g., `en-US-Ava:DragonHDLatestNeural` |
| `DisplayName` | `string` | Short name, e.g., `Ava` |
| `Locale` | `string` | BCP-47 locale, e.g., `en-US` |
| `Gender` | `string` | `Male` or `Female` |

**Static members**:
- `VoiceLiveVoices.All` — `IReadOnlyList<VoiceLiveVoiceInfo>` of all voices
- `VoiceLiveVoices.ByLocale` — `IReadOnlyDictionary<string, IReadOnlyList<VoiceLiveVoiceInfo>>`
- `VoiceLiveVoices.ValidNames` — `HashSet<string>` of all `FullName` values for validation
- `VoiceLiveVoices.Locales` — `IReadOnlyList<string>` of distinct locales

**Initial catalog** (selected GA + key Preview voices):

| Locale | Voices |
|--------|--------|
| en-US | Adam (M), Andrew (M), Ava (F), Brian (M), Davis (M), Emma (F), Jenny (F), Nova (F), Aria (F), Alloy (M), Phoebe (F), Steffan (M) |
| de-DE | Florian (M), Seraphina (F) |
| es-ES | Tristan (M), Ximena (F) |
| fr-FR | Remy (M), Vivienne (F) |
| ja-JP | Masaru (M), Nanami (F) |
| zh-CN | Xiaochen (F), Yunfan (M) |

---

### CallRecord (MODIFY existing)

**File**: `ContactCenter-API/Models/CallRecord.cs`

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| All existing fields | — | — | No changes |
| `VoiceApiMode` | `string` | `"ChatGPT"` | **NEW** — persisted for call history |
| `VoiceLiveModel` | `string?` | `null` | **NEW** — persisted when VoiceLive used |
| `VoiceLiveVoice` | `string?` | `null` | **NEW** — persisted when VoiceLive used |

---

### VoiceLiveConfig (NEW — app settings binding)

**File**: `ContactCenter-API/Models/VoiceLiveConfig.cs`
**Bound from**: `appsettings.json` → `VoiceLive` section

| Property | Type | Default | Notes |
|----------|------|---------|-------|
| `EndpointUri` | `string` | `""` | VoiceLive service endpoint |
| `Key` | `string` | `""` | Optional API key (empty = use Managed Identity) |

```csharp
public class VoiceLiveConfig
{
    public string EndpointUri { get; set; } = "";
    public string Key { get; set; } = "";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(EndpointUri);
}
```

---

## Settings API Request/Response Changes

### GET /api/Settings — Response

```json
{
  "maxCallTimeMinutes": 2.0,
  "voiceApiMode": "VoiceLive",
  "selectedVoice": "alloy",
  "transcriptionMode": "BuiltIn",
  "voiceLiveModel": "gpt-4o",
  "selectedVoiceLiveVoice": "en-US-Ava:DragonHDLatestNeural",
  "voiceLiveConfigured": true,
  "availableVoiceLiveVoices": [
    { "fullName": "en-US-Ava:DragonHDLatestNeural", "displayName": "Ava", "locale": "en-US", "gender": "Female" },
    { "fullName": "en-US-Adam:DragonHDLatestNeural", "displayName": "Adam", "locale": "en-US", "gender": "Male" }
  ],
  "availableVoiceLiveModels": ["gpt-realtime", "gpt-4o", "gpt-4.1", "gpt-5", "phi-4-mini"]
}
```

### PUT /api/Settings — Request

```json
{
  "maxCallTimeMinutes": 2.0,
  "voiceApiMode": "VoiceLive",
  "selectedVoice": "alloy",
  "transcriptionMode": "BuiltIn",
  "voiceLiveModel": "gpt-4o",
  "selectedVoiceLiveVoice": "en-US-Ava:DragonHDLatestNeural"
}
```

---

## SignalR Events (changes)

### CallStatusChanged (MODIFY existing)

New status values broadcast via `TranscriptHub`:

| Status | When |
|--------|------|
| `Reconnecting` | **NEW** — VoiceLive WebSocket disconnected, reconnect attempt in progress |
| `ReconnectFailed` | **NEW** — All 3 reconnect attempts failed, call ending |

Payload (unchanged structure):
```json
{
  "callConnectionId": "abc123",
  "status": "Reconnecting",
  "message": "Reconnecting… attempt 2/3"
}
```
