# Data Model: Outbound Call Center POC

**Feature**: `001-outbound-callcenter-poc`  
**Date**: 2026-02-20  
**Updated**: 2026-02-21  
**Source**: Extracted from [spec.md](spec.md) Key Entities section

## Entities

### 1. CallRequest (Input DTO)

Submitted by the operator to initiate one or two simultaneous calls.

| Field | Type | Validation | Description |
|-------|------|------------|-------------|
| PhoneNumbers | string[] | Required. 1–2 items. Each E.164 format: `^\+[1-9]\d{1,14}$` | Target phone number(s) to call simultaneously |
| CampaignId | string? | Optional (if null, uses Prompt; if both null, uses default system prompt) | ID of the selected campaign |
| Prompt | string? | Optional (overrides campaign instructions if provided) | Custom AI agent instructions for this call |

**Note**: `PhoneNumbers` replaces the original single `PhoneNumber` field to support multi-number calling (FR-018). The API still accepts 1 number for backward compatibility. If both `CampaignId` and `Prompt` are provided, `Prompt` takes precedence. If neither is provided, the default system prompt from configuration is used.

### 2. ActiveCall (In-Memory Runtime State)

Tracks a single outbound call while it is active. Not persisted to a database — held in a `ConcurrentDictionary<string, ActiveCall>` on the `CallService` singleton.

| Field | Type | Description |
|-------|------|-------------|
| CallConnectionId | string | ACS call connection identifier (primary key) |
| ServerCallId | string? | ACS server call identifier (used for recording). Null until `CallConnected` event is received. |
| TargetPhoneNumber | string | E.164 phone number of the recipient |
| CampaignId | string? | ID of the campaign used for this call (null if custom prompt) |
| CampaignTitle | string? | Title of the campaign (for display/logging) |
| Prompt | string | AI prompt assigned to this call (resolved from campaign or custom) |
| Status | CallStatus (enum) | Current lifecycle state |
| StartedAt | DateTimeOffset | UTC timestamp when call was initiated |
| RecordingId | string? | ACS recording ID (set on CallConnected, null before) |
| TranscriptEntries | List\<TranscriptEntry\> | Accumulated transcript for this call (for persistence on disconnect) |
| CancellationTokenSource | CTS | Per-call CTS with 5-minute CancelAfter |

#### CallStatus Enum

| Value | Description |
|-------|-------------|
| Initiating | Call creation request sent to ACS |
| Ringing | ACS is ringing the target number. **Note**: Currently unused — ACS does not fire a discrete ringing callback in this flow. Reserved for future use. |
| Connected | Call answered; audio streaming + AI active |
| Disconnected | Call ended (by recipient, operator, timeout, or error) |

### 3. Campaign (Persistent Configuration)

Reusable call configuration defining the AI agent's behavior. Stored as a JSON file in Azure Blob Storage (or local file for development). 4 pre-defined campaigns ship by default; operator can create additional campaigns at runtime.

| Field | Type | Validation | Description |
|-------|------|------------|-------------|
| Id | string | Required. Auto-generated GUID | Unique identifier |
| Title | string | Required. Unique across all campaigns | Display name (e.g., "Loan Collections") |
| Description | string | Required | What the campaign is about (shown in UI) |
| AiBehaviorInstructions | string | Required | System prompt for the AI agent |
| IsDefault | bool | — | True for the 4 pre-defined campaigns (cannot be deleted) |
| CreatedAt | DateTimeOffset | — | UTC timestamp when campaign was created |

**Note**: Replaces the original `PromptScenario` entity. Campaigns are persisted to Blob Storage as a single JSON file (`campaigns.json`) and cached in memory on load. Changes write-through to Blob Storage.

### 4. TranscriptEntry (Streamed via SignalR + Persisted)

A single line of transcript streamed to the operator in real time. Also accumulated on the `ActiveCall` for persistence to call history when the call ends.

| Field | Type | Description |
|-------|------|-------------|
| CallConnectionId | string | Which call this transcript belongs to |
| Speaker | SpeakerType (enum) | AI or Recipient |
| Text | string | Transcribed speech content |
| Sentiment | SentimentResult | Sentiment analysis of this segment |
| Timestamp | DateTimeOffset | When the utterance was captured |

#### SpeakerType Enum

| Value | Description |
|-------|-------------|
| AI | Text spoken by the AI agent |
| Recipient | Text spoken by the call recipient |

### 5. SentimentResult (Value Object)

Sentiment analysis result for a single transcript segment. Computed in real time by `SentimentAnalysisService` using Azure OpenAI chat completion.

| Field | Type | Description |
|-------|------|-------------|
| Label | SentimentLabel (enum) | Sentiment classification |
| Confidence | float | Confidence score (0.0–1.0, optional for POC) |

#### SentimentLabel Enum

| Value | Description |
|-------|-------------|
| Positive | Text expresses satisfaction, agreement, happiness |
| Neutral | Text is informational, factual, or unremarkable |
| Negative | Text expresses frustration, disagreement, dissatisfaction |

### 6. CallStatusUpdate (Streamed via SignalR)

Status change notification sent to the operator's browser.

| Field | Type | Description |
|-------|------|-------------|
| CallConnectionId | string | Which call changed status |
| Status | CallStatus | New status value |
| Message | string? | Human-readable detail (e.g., "Call ended: 5-minute limit reached") |
| Timestamp | DateTimeOffset | When the status changed |

### 7. CallRecord (Persisted to Blob Storage)

A completed call record persisted to Azure Blob Storage as a JSON file for call history review. Created when a call disconnects by serializing relevant data from the `ActiveCall`.

| Field | Type | Description |
|-------|------|-------------|
| CallConnectionId | string | Primary key — matches the ACS call connection ID |
| PhoneNumber | string | E.164 phone number of the recipient |
| CampaignId | string? | Campaign used for the call (null if custom prompt) |
| CampaignTitle | string? | Campaign title at time of call (snapshot) |
| Prompt | string | Resolved AI prompt used for the call |
| Duration | TimeSpan | Call duration (EndedAt − StartedAt) |
| OverallSentiment | SentimentLabel | Aggregated sentiment across all transcript entries |
| SentimentBreakdown | SentimentBreakdown | Percentage breakdown of positive/neutral/negative |
| TalkTimeRatio | TalkTimeRatio | Talk time split between AI and Recipient |
| TranscriptEntries | List\<TranscriptEntry\> | Full transcript with per-segment sentiment |
| StartedAt | DateTimeOffset | When the call was initiated |
| EndedAt | DateTimeOffset | When the call disconnected |

#### SentimentBreakdown (Value Object)

| Field | Type | Description |
|-------|------|-------------|
| PositivePercent | float | % of transcript entries with Positive sentiment |
| NeutralPercent | float | % of transcript entries with Neutral sentiment |
| NegativePercent | float | % of transcript entries with Negative sentiment |

#### TalkTimeRatio (Value Object)

| Field | Type | Description |
|-------|------|-------------|
| AiPercent | float | % of total transcript entries spoken by AI |
| RecipientPercent | float | % of total transcript entries spoken by Recipient |

### 8. Recording (Managed by ACS)

Call recordings are managed entirely by ACS Call Automation. The application stores only the `RecordingId` in `ActiveCall` for the purpose of stopping recording on disconnect. Actual audio files are persisted to the configured Azure Blob Storage container by ACS.

| Field | Type | Description |
|-------|------|-------------|
| RecordingId | string | ACS recording identifier |
| ServerCallId | string | Associated server call ID |
| StorageUri | string | Azure Blob Storage URI (from `BlobContainer` config) |

## Relationships

```
Operator (browser) ──[submits]──> CallRequest
                                    │    (1–2 PhoneNumbers)
                                    ▼
                              ActiveCall (1 per phone number, max 5 total)
                                    │
                         ┌──────────┼──────────┐──────────────┐
                         ▼          ▼          ▼              ▼
                   Recording   AI Session   TranscriptEntry[] SentimentResult
                  (1 per call) (1 per call)  (N per call)     (1 per entry)
                                    │
                                    ▼
                            CallStatusUpdate[]
                             (streamed to browser)
                                    
                         ┌──────────────────────┐
                         │   On Disconnect:     │
                         │   ActiveCall ──────> CallRecord (persisted to Blob)
                         └──────────────────────┘

Campaign[] (persisted to Blob — campaigns.json)
    │
    └──[selected by operator]──> CallRequest.CampaignId
                                    │
                                    └──[resolved to]──> ActiveCall.Prompt
```

## State Transitions

```
                    ┌─────────────┐
  POST /api/Call/   │             │
  initiate ────────>│  Initiating │  (×1 or ×2, one per phone number)
                    │             │
                    └──────┬──────┘
                           │ ACS starts ringing
                           ▼
                    ┌─────────────┐
                    │   Ringing   │──── no answer / error ──────┐
                    └──────┬──────┘                              │
                           │ Recipient answers                   │
                           ▼                                     │
                    ┌─────────────┐                              │
                    │  Connected  │                              │
                    │  (recording │                              │
                    │   + AI +    │                              │
                    │  transcript │                              │
                    │  +sentiment)│                              │
                    └──────┬──────┘                              │
                           │ hang-up / timeout / error           │
                           ▼                                     ▼
                    ┌──────────────┐                     ┌──────────────┐
                    │ Disconnected │                     │ Disconnected │
                    │ (cleanup +   │                     │ (cleanup)    │
                    │  persist     │                     └──────────────┘
                    │  CallRecord) │
                    └──────────────┘
```

## Persistence Strategy

| Data | Storage | Format | Lifecycle |
|------|---------|--------|-----------|
| Call recordings | Azure Blob Storage (ACS managed) | Audio files | Created on CallConnected, stopped on Disconnect |
| Campaigns | Azure Blob Storage | `campaigns.json` (single file) | Loaded on startup, write-through on create |
| Call history (CallRecords) | Azure Blob Storage | `call-history/{callConnectionId}.json` (one file per call) | Created on Disconnect |
| Active calls | In-memory `ConcurrentDictionary` | Runtime objects | Lifetime of call only |
| Transcript (live) | In-memory on `ActiveCall` | List\<TranscriptEntry\> | Accumulated during call, persisted on Disconnect |

## Constraints

- Maximum 5 `ActiveCall` entries at any time (FR-017)
- Maximum 2 phone numbers per `CallRequest` (FR-018)
- Each `ActiveCall` auto-terminates after 5 minutes via its `CancellationTokenSource` (FR-016)
- `CallConnectionId` is unique across all active calls
- Campaign `Title` must be unique across all campaigns
- Default campaigns (IsDefault=true) cannot be deleted
- Runtime state (active calls) is in-memory; persistent data (campaigns, call history) is in Azure Blob Storage
- Sentiment analysis must complete within 1 second of transcript receipt (SC-009)
- Call history must load within 3 seconds (SC-010)
