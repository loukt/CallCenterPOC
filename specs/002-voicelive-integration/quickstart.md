# Quickstart: Azure AI VoiceLive Integration

**Feature**: 002-voicelive-integration | **Date**: 2026-03-05

## Prerequisites

- .NET 9 SDK
- Azure subscription with an Azure AI Speech or Microsoft Foundry resource that has VoiceLive enabled
- Existing CallCenterPOC deployment working with ChatGPT Realtime

## 1. Install NuGet Package

```bash
dotnet add ContactCenter-API/ContactCenter-API.csproj package Azure.AI.VoiceLive --version 1.0.0
```

## 2. Configure VoiceLive Endpoint

### Option A: App Settings (local development)

Add to `ContactCenter-API/appsettings.json`:

```json
{
  "VoiceLive": {
    "EndpointUri": "https://<your-resource>.services.ai.azure.com",
    "Key": ""
  }
}
```

### Option B: User Secrets (local development with API key)

```bash
cd ContactCenter-API
dotnet user-secrets set "VoiceLive:EndpointUri" "https://<your-resource>.services.ai.azure.com"
dotnet user-secrets set "VoiceLive:Key" "<your-api-key>"
```

### Option C: Azure App Service (production — recommended)

Set these application settings in the Azure portal or via CLI:

```bash
az webapp config appsettings set --name <app-name> --resource-group <rg> --settings \
  VoiceLive__EndpointUri="https://<your-resource>.services.ai.azure.com"
```

No key needed when using Managed Identity (recommended).

## 3. Configure RBAC for Managed Identity

The App Service's system-assigned Managed Identity needs these roles on the VoiceLive resource:

```bash
# Get the App Service's Managed Identity principal ID
PRINCIPAL_ID=$(az webapp identity show --name <app-name> --resource-group <rg> --query principalId -o tsv)

# Assign roles on the Azure AI / Speech resource
az role assignment create \
  --assignee $PRINCIPAL_ID \
  --role "Cognitive Services User" \
  --scope /subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.CognitiveServices/accounts/<resource>

az role assignment create \
  --assignee $PRINCIPAL_ID \
  --role "Azure AI User" \
  --scope /subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.CognitiveServices/accounts/<resource>
```

## 4. Verify Configuration

After deploying, check the health endpoint:

```bash
curl https://<your-api>/healthz
```

Expected response when configured:
```json
{
  "status": "Healthy",
  "voiceLive": {
    "configured": true,
    "endpoint": "*.services.ai.azure.com"
  }
}
```

## 5. Enable VoiceLive in the UI

1. Open the operator dashboard
2. Click the **Settings** gear icon
3. Under **Voice API Mode**, select **Voice Live** (previously disabled)
4. Select a **Dragon HD voice** from the voice dropdown (e.g., "Ava (en-US)")
5. Select an **AI Model** (e.g., "GPT-4o")
6. Optionally change **Transcription Source** to "Separate STT Pipeline"
7. Click **Save**

## New Configuration Keys Reference

| Key | Type | Required | Default | Description |
|-----|------|----------|---------|-------------|
| `VoiceLive:EndpointUri` | string | Yes (for VoiceLive) | `""` | Azure AI VoiceLive service endpoint |
| `VoiceLive:Key` | string | No | `""` | API key (empty = use Managed Identity) |

## Supported Regions

VoiceLive with Dragon HD voices is available in: **southeastasia**, centralindia, swedencentral, westeurope, eastus, eastus2, westus2.

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| "Voice Live" radio button shows "Not Configured" | `VoiceLive:EndpointUri` not set | Add endpoint to app settings |
| Authentication error on call start | Missing RBAC roles | Assign "Cognitive Services User" + "Azure AI User" to Managed Identity |
| No audio during VoiceLive call | Audio format mismatch | Verify ACS media streaming is using PCM 16-bit format |
| "Reconnecting" status during call | VoiceLive WebSocket disconnected | Check network stability; system auto-retries up to 3 times |
