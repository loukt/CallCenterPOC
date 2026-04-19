[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter()]
    [string]$Repository = "loukt/CallCenterPOC",

    [Parameter(Mandatory = $true)]
    [string]$AzureCredentialsFile,

    [Parameter()]
    [string]$ApiAppSettingsFile = "prod-settings.json"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw "GitHub CLI (gh) is required but was not found in PATH."
}

if (-not (Test-Path -LiteralPath $AzureCredentialsFile)) {
    throw "Azure credentials file '$AzureCredentialsFile' was not found."
}

if (-not (Test-Path -LiteralPath $ApiAppSettingsFile)) {
    throw "API app settings file '$ApiAppSettingsFile' was not found."
}

$azureCredentials = Get-Content -LiteralPath $AzureCredentialsFile -Raw
$apiAppSettings = Get-Content -LiteralPath $ApiAppSettingsFile -Raw

if ([string]::IsNullOrWhiteSpace($azureCredentials)) {
    throw "Azure credentials file '$AzureCredentialsFile' is empty."
}

if ([string]::IsNullOrWhiteSpace($apiAppSettings)) {
    throw "API app settings file '$ApiAppSettingsFile' is empty."
}

$null = $azureCredentials | ConvertFrom-Json
$null = $apiAppSettings | ConvertFrom-Json

Write-Host "Preparing GitHub Actions secrets for '$Repository'."
Write-Host "Secrets to update:"
Write-Host " - AZURE_CREDENTIALS"
Write-Host " - AZURE_API_APPSETTINGS_JSON"

if ($PSCmdlet.ShouldProcess($Repository, "Set GitHub Actions secrets")) {
    $azureCredentials | gh secret set AZURE_CREDENTIALS --repo $Repository
    $apiAppSettings | gh secret set AZURE_API_APPSETTINGS_JSON --repo $Repository
    Write-Host "GitHub Actions secrets updated successfully."
}