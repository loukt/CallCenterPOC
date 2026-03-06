# CallCenterPOC Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-02-20

## Active Technologies
- C# / .NET 9.0 (001-outbound-call-sim)
- N/A for MVP (in-memory state); optional Azure Blob Storage for recording BYOS (001-outbound-call-sim)
- C# / .NET 9 (ASP.NET Core) + ASP.NET Core, Razor Pages, Azure Communication Services (Call Automation), Azure OpenAI .NET SDK (001-call-review-analytics)
- Azure Blob Storage for call audio (BYOS) and artifact JSON (transcript + analytics) with optional local dev fallback (001-call-review-analytics)
- C# / .NET 9.0 + Azure.Communication.CallAutomation 1.4.0-beta.1, Azure.AI.OpenAI 2.1.0-beta.2 (OpenAI.RealtimeConversation), Azure.Messaging.EventGrid 4.29.0, Swashbuckle.AspNetCore 6.6.2, Newtonsoft.Json 13.0.3 (001-outbound-callcenter-poc)
- Azure Blob Storage (call recordings via ACS managed recording) (001-outbound-callcenter-poc)
- C# / .NET 9 (net9.0) + Azure.AI.OpenAI 2.1.0-beta.2, Azure.Communication.CallAutomation 1.4.0-beta.1, Azure.Identity 1.17.1, Azure.Storage.Blobs 12.23.0, SignalR, **NEW: Azure.AI.VoiceLive 1.0.0 GA** (002-voicelive-integration)
- Azure Blob Storage (JSON blobs for settings, campaigns, call history) (002-voicelive-integration)

- ASP.NET Core Web API + Razor Pages (CallCenterPOC)
- Azure Communication Services Call Automation (outbound PSTN + callbacks + WebSocket media streaming)
- Azure OpenAI Realtime voice (`Azure.AI.OpenAI` experimental realtime APIs)

## Project Structure

```text
ContactCenter-APP/
ContactCenter-API/
specs/
```

## Commands

```powershell
# Build
dotnet build .\CallCenterPOC.sln

# Run API
dotnet run --project .\ContactCenter-API

# Run UI
dotnet run --project .\ContactCenter-APP
```

## Code Style

- C#: follow standard .NET conventions; keep controllers thin and services encapsulated.
- Never commit secrets; prefer `dotnet user-secrets` for local development.

## Recent Changes
- 002-voicelive-integration: Added C# / .NET 9 (net9.0) + Azure.AI.OpenAI 2.1.0-beta.2, Azure.Communication.CallAutomation 1.4.0-beta.1, Azure.Identity 1.17.1, Azure.Storage.Blobs 12.23.0, SignalR, **NEW: Azure.AI.VoiceLive 1.0.0 GA**
- 001-outbound-callcenter-poc: Added C# / .NET 9.0 + Azure.Communication.CallAutomation 1.4.0-beta.1, Azure.AI.OpenAI 2.1.0-beta.2 (OpenAI.RealtimeConversation), Azure.Messaging.EventGrid 4.29.0, Swashbuckle.AspNetCore 6.6.2, Newtonsoft.Json 13.0.3
- 001-outbound-callcenter-poc: Added C# / .NET 9.0 + Azure.Communication.CallAutomation 1.4.0-beta.1, Azure.AI.OpenAI 2.1.0-beta.2 (OpenAI.RealtimeConversation), Azure.Messaging.EventGrid 4.29.0, Swashbuckle.AspNetCore 6.6.2, Newtonsoft.Json 13.0.3

<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
