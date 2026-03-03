# Quickstart: Outbound Call Center POC

**Feature**: `001-outbound-callcenter-poc`  
**Date**: 2026-02-25

## Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- An Azure subscription with:
  - **Azure Communication Services** resource with a provisioned phone number
  - **Azure OpenAI** resource with:
    - A `gpt-4o-realtime-preview` deployment (for voice conversations)
    - A `gpt-4o-mini` deployment (for sentiment analysis)
  - **Azure Blob Storage** account with a container for recordings, campaigns, and call history
- A tunneling tool (e.g., [ngrok](https://ngrok.com/), [devtunnels](https://learn.microsoft.com/en-us/azure/developer/dev-tunnels/)) to expose the API publicly for ACS callbacks

## Configuration

### ContactCenter-API (Backend)

Set the following in **User Secrets** (do NOT put secrets in `appsettings.json`):

```bash
dotnet user-secrets set "AzureCommunicationServices:ConnectionString" "endpoint=https://xxx.communication.azure.com/;accesskey=xxx"
dotnet user-secrets set "AzureCommunicationServices:PhoneNumber" "+1234567890"
dotnet user-secrets set "AzureOpenAI:Key" "your-azure-openai-key"
dotnet user-secrets set "BlobContainer" "https://yourstorage.blob.core.windows.net/recordings"
dotnet user-secrets set "BlobStorage:ConnectionString" "DefaultEndpointsProtocol=https;AccountName=xxx;AccountKey=xxx;EndpointSuffix=core.windows.net"
```

Set the following in `appsettings.json` (non-secret):

| Key | Description | Example |
|-----|-------------|---------|
| `CallbackUrl` | Public HTTPS URL where ACS sends callbacks | `https://abc123.ngrok-free.app/api/Callback` |
| `AzureOpenAI:EndpointUri` | Azure OpenAI endpoint URI | `https://your-resource.openai.azure.com/` |
| `AzureOpenAI:DeploymentName` | Model deployment name (Realtime voice) | `gpt-4o-realtime-preview` |
| `AzureOpenAI:ChatDeployment` | Model deployment name (Sentiment analysis) | `gpt-4o-mini` |
| `AzureOpenAI:TranscriptionDeployment` | Model deployment name (Recording transcription) | `gpt-4o-mini-transcribe` or `whisper-1` |
| `AzureOpenAI:TranscriptionApiVersion` | API version override for transcription endpoint (optional) | `2024-10-21` |
| `AzureOpenAI:TranscriptionLanguage` | Optional language hint (ISO-639-1) | `en` |
| `AzureOpenAI:SystemPrompt` | Default system prompt (fallback when no campaign/prompt selected) | `"You are an AI assistant..."` |
| `AzureOpenAI:EmotionDeployment` | Optional separate deployment for emotion analysis (falls back to ChatDeployment) | `gpt-4o-mini` |
| `AzureOpenAI:Version` | API version | `2024-10-01-preview` |
| `BlobStorage:AccountUri` | Blob Storage account URI (Azure deployment with Managed Identity) | `https://yourstorage.blob.core.windows.net` |
| `BlobStorage:ContainerName` | Container name for campaigns + call history | `callcenter-data` |

**Note (Transcription Auth):** On-demand recording transcription uses `DefaultAzureCredential` (Entra ID) and does **not** use `AzureOpenAI:Key`. For local development, run `az login` and ensure your signed-in identity has access to the Azure OpenAI resource (e.g., â€œCognitive Services OpenAI Userâ€).

### ContactCenter-APP (Frontend)

Set in `appsettings.json`:

| Key | Description | Example |
|-----|-------------|---------|
| `ApiBaseUrl` | URL of the backend API | `https://localhost:5001` or `https://abc123.ngrok-free.app` |

## Running Locally

### 1. Start the tunnel

```bash
# Using ngrok:
ngrok http 5001

# Note the HTTPS forwarding URL (e.g., https://abc123.ngrok-free.app)
# Update CallbackUrl in API appsettings.json to: https://abc123.ngrok-free.app/api/Callback
# Update ApiBaseUrl in App appsettings.json to: https://abc123.ngrok-free.app
```

### 2. Start the API

```bash
cd ContactCenter-API
dotnet run
```

The API starts on `https://localhost:5001` by default. Swagger UI is available at `/swagger`.

### 3. Start the Web App

```bash
cd ContactCenter-APP
dotnet run
```

The web app starts on `https://localhost:5002` (check `launchSettings.json`).

### 4. Use the Professional Operations Dashboard

The web app presents a **professional three-panel operations center** with a deep navy theme:

| Panel | Location | Purpose |
|-------|----------|---------|
| **Left (Campaigns)** | Left sidebar (320px) | Campaign cards with colored category badges, search/filter, create form, phone inputs, prompt override |
| **Center (Live Call)** | Main area | KPI summary cards (idle), call controls bar, chat-bubble transcript, sentiment graph, quick responses |
| **Right (History)** | Right sidebar (320px) | Active call info card, call history with expandable detail view |

#### Default Campaigns (6 Outbound Types)

| Campaign | Category | Badge Color |
|----------|----------|-------------|
| Bank Loan Collection | Collections | Amber |
| New Product Marketing | Marketing | Blue |
| Customer Satisfaction Survey | Survey | Green |
| Appointment Reminder | Reminder | Purple |
| Insurance Policy Renewal | Renewal | Teal |
| Subscription Renewal & Upsell | Upsell | Orange |

Each campaign has detailed AI behavior instructions (100+ characters) guiding the conversation tone and approach.

#### Making a Call

1. Open the web app in Chrome/Edge
2. **Left panel**: Browse campaign cards with colored category badges, or use the search bar to filter
3. Select a campaign â€” the card highlights with an accent border
4. Enter 1 or 2 phone numbers in E.164 format (e.g., `+6591234567`)
5. Optionally expand "Prompt Override" to customize AI instructions
6. Click **Start Call**
7. Answer the phone â€” the AI agent will greet you and conduct a conversation

#### Live Call Experience

8. **Call Controls Bar**: End Call (red, circular), Mute (toggle mic on/off), Hold (toggle pause/resume), mic level pulse indicator
9. **Chat-Bubble Transcript**: AI messages (left, blue background, robot avatar) and Caller messages (right, white background, person avatar) with timestamps and sentiment dots (green/gray/red)
10. **Sentiment Graph**: Canvas-based rolling line chart updates with each transcript entry
11. **Quick Responses**: Category-specific suggested phrases (click to copy to clipboard with toast notification)
12. **Call Timer**: MM:SS timer starts on connected, displayed in status bar
13. For **multi-call** scenarios (2 phone numbers), use the **call tabs** to switch between calls

#### KPI Summary Cards (Idle State)

| KPI | Description | Persistence |
|-----|-------------|-------------|
| Calls Today | Total completed calls in session | sessionStorage |
| Avg Duration | Mean call duration (MM:SS) | sessionStorage |
| Sentiment | Average positive sentiment percentage | sessionStorage |
| Success Rate | Connected vs total calls | sessionStorage |

14. Click **End Call** to end the call, or wait for the 5-minute auto-timeout
15. **Right panel**: Review completed calls â€” click any history item for details with sentiment breakdown bars, talk-time ratio, and full transcript

#### Transcribe Existing Recordings (On-Demand)

For historical calls that have a stored MP3/WAV recording in Blob Storage, you can attach a transcript to the call record:

1. **Right panel (History)**: Expand a completed call
2. In **Recording Transcript**, click **Transcribe**
3. Wait for completion â€” the transcript text appears and is persisted into the call history JSON (remains after refresh)

API endpoint (for troubleshooting): `POST /api/CallHistory/{callConnectionId}/transcribe?force=false`

**Mobile/narrow screens (<992px):** The layout switches to a stacked view with a bottom **tab bar** (Campaigns / Live Call / History icons) for panel switching.

## Configuration Reference

| Config Key | Project | Secret? | Required? | Description |
|------------|---------|---------|-----------|-------------|
| `AzureCommunicationServices:ConnectionString` | API | Yes | Yes | ACS resource connection string |
| `AzureCommunicationServices:PhoneNumber` | API | Yes | Yes | Provisioned caller phone number (E.164) |
| `CallbackUrl` | API | No | Yes | Public HTTPS callback URL for ACS events |
| `AzureOpenAI:Key` | API | Yes | Yes | Azure OpenAI API key (local dev only; use Managed Identity in Azure) |
| `AzureOpenAI:EndpointUri` | API | No | Yes | Azure OpenAI endpoint URI |
| `AzureOpenAI:DeploymentName` | API | No | Yes | Realtime voice model deployment name |
| `AzureOpenAI:ChatDeployment` | API | No | Yes | Chat model deployment for sentiment analysis (e.g., `gpt-4o-mini`) |
| `AzureOpenAI:TranscriptionDeployment` | API | No | No | Audio transcription deployment name (e.g., `gpt-4o-mini-transcribe` or `whisper-1`) |
| `AzureOpenAI:TranscriptionApiVersion` | API | No | No | Optional API version override for `/audio/transcriptions` |
| `AzureOpenAI:TranscriptionLanguage` | API | No | No | Optional transcription language hint (ISO-639-1 like `en`) |
| `AzureOpenAI:Version` | API | No | No | API version (defaults to latest preview) |
| `AzureOpenAI:SystemPrompt` | API | No | No | Default fallback system prompt |
| `BlobContainer` | API | No | No | Azure Blob Storage container URL for recordings |
| `BlobStorage:ConnectionString` | API | Yes | No | Blob Storage connection string (local dev; use Managed Identity in Azure) |
| `BlobStorage:AccountUri` | API | No | No | Blob Storage account URI (Azure deployment with Managed Identity) |
| `BlobStorage:ContainerName` | API | No | No | Container name for campaigns + call history (default: `callcenter-data`) |
| `FrontendOrigin` | API | No | No | Allowed CORS origin for SignalR (defaults to `https://localhost:5002`) |
| `CallHistory:CacheTtlSeconds` | API | No | No | How long to cache call history list responses, in seconds (default: `30`) |
| `AzureOpenAI:EmotionDeployment` | API | No | No | Chat deployment for emotion analysis (falls back to `ChatDeployment`) |
| `ApiBaseUrl` | App | No | Yes | Backend API URL |

## Dual-Speaker Emotion Graphs & Operator Style Traits (Phase 22)

Phase 22 adds real-time emotion classification for every transcript segment plus end-of-call operator style analysis.

### What's New

| Feature | Description |
|---------|-------------|
| **Emotion Analysis** | Each transcript entry (both AI operator and customer) is classified with one of 6 emotions: Neutral, Happy, Frustrated, Angry, Sad, Anxious |
| **Dual Emotion Graphs** | Two new rolling canvas graphs below the existing Sentiment Timeline — one for Operator Emotion, one for Customer Emotion |
| **Operator Style Traits** | At end-of-call, AI-side transcript is analyzed for Empathy (0–1) and Energy (0–1) scores |
| **EmotionUpdate SignalR Event** | Real-time push updates to the dashboard when emotion analysis completes for an entry |
| **Historical Emotion Data** | Emotion labels persist on each `TranscriptEntry` in blob storage and display in the call detail view |

### Configuration

Emotion analysis uses the same Azure OpenAI chat completions endpoint as sentiment analysis. By default, it reuses the `AzureOpenAI:ChatDeployment` deployment.

To use a dedicated deployment for emotion analysis, set:

```bash
dotnet user-secrets set "AzureOpenAI:EmotionDeployment" "gpt-4o-mini-emotion"
```

Or in `appsettings.json`:

```json
{
  "AzureOpenAI": {
    "EmotionDeployment": "gpt-4o-mini"
  }
}
```

If `EmotionDeployment` is not set, the `ChatDeployment` value is used automatically.

### Dashboard UI

- **Operator Emotion Graph**: Colored dots on a rolling timeline (green=Happy, grey=Neutral, orange=Frustrated, red=Angry, blue=Sad, purple=Anxious)
- **Customer Emotion Graph**: Same format, tracking the customer/recipient side
- **Call Detail View**: Each transcript entry now shows an emotion badge alongside the existing sentiment badge
- **Operator Style Section**: Empathy and Energy progress bars appear in the historical call detail when operator style traits were computed

### Verification

1. Start a call — confirm two new emotion graphs appear below the Sentiment Timeline
2. As the conversation progresses, colored emotion dots populate both graphs
3. End the call, then view it in History — confirm emotion badges on transcript entries
4. If the operator spoke enough, confirm "Operator Style" section appears with Empathy and Energy bars

## Verification

After setup, verify the system works:

1. Navigate to `https://localhost:5001/swagger` â€” API docs should load with Call, Campaign, CallHistory endpoints
2. Navigate to `https://localhost:5002` â€” **professional three-panel Operations Center** with deep navy header, KPI cards, and campaign cards with colored category badges
3. **Left panel**: Select a campaign (6 pre-defined outbound campaigns with category badges, or create a custom one)
4. Initiate a test call to a phone you can answer
5. Confirm: phone rings, AI speaks, you can respond, chat-bubble transcript with sentiment dots and live sentiment graph appears, call controls (end/mute/hold) work, quick responses are available
6. **Right panel**: Verify the completed call appears in history â€” click it for details with sentiment breakdown, talk-time ratio, and full transcript
7. **KPI Cards**: Verify Calls Today, Avg Duration, Sentiment, and Success Rate update after the call
8. If the call has a recording: click **Transcribe** in the call detail panel and confirm the transcript persists after refresh

