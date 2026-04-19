[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroup,

    [Parameter(Mandatory = $true)]
    [string]$AppName,

    [Parameter()]
    [string]$SettingsFile = "prod-settings.json",

    [Parameter()]
    [string]$Slot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
    throw "Azure CLI (az) is required but was not found in PATH."
}

if (-not (Test-Path -LiteralPath $SettingsFile)) {
    throw "Settings file '$SettingsFile' was not found."
}

$raw = Get-Content -LiteralPath $SettingsFile -Raw
$settings = $raw | ConvertFrom-Json

if ($null -eq $settings) {
    throw "Settings file '$SettingsFile' is empty or invalid JSON."
}

if ($settings -isnot [System.Collections.IEnumerable]) {
    throw "Settings file '$SettingsFile' must contain a JSON array of { name, value, slotSetting }."
}

$settingsList = @($settings)
if ($settingsList.Count -eq 0) {
    throw "Settings file '$SettingsFile' does not contain any settings."
}

$invalid = @($settingsList | Where-Object {
    [string]::IsNullOrWhiteSpace($_.name) -or $null -eq $_.value
})

if ($invalid.Count -gt 0) {
    throw "Every setting entry must contain non-empty 'name' and 'value' properties."
}

$placeholderPattern = '^<.+>$'
$placeholderNames = @($settingsList |
    Where-Object {
        $_.value -is [string] -and $_.value -match $placeholderPattern
    } |
    Select-Object -ExpandProperty name)

if ($placeholderNames.Count -gt 0) {
    Write-Warning "The settings file still contains placeholder values for: $($placeholderNames -join ', ')"
}

$settingsArgs = foreach ($setting in $settingsList) {
    "$($setting.name)=$($setting.value)"
}

$target = if ([string]::IsNullOrWhiteSpace($Slot)) {
    "$AppName"
}
else {
    "$AppName/$Slot"
}

Write-Host "Applying $($settingsList.Count) app settings to '$target' in resource group '$ResourceGroup'."
Write-Host "Setting names:"
$settingsList.name | Sort-Object | ForEach-Object { Write-Host " - $_" }

$command = @(
    "webapp", "config", "appsettings", "set",
    "--resource-group", $ResourceGroup,
    "--name", $AppName
)

if (-not [string]::IsNullOrWhiteSpace($Slot)) {
    $command += @("--slot", $Slot)
}

$command += @("--settings")
$command += $settingsArgs
$command += @("--output", "none", "--only-show-errors")

if ($PSCmdlet.ShouldProcess($target, "Apply App Service settings from $SettingsFile")) {
    az @command
    Write-Host "App settings applied successfully."
}