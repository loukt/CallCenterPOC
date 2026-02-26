param(
  [Parameter(Mandatory=$true)]
  [string]$Source,

  [Parameter(Mandatory=$true)]
  [string]$Out
)

$ErrorActionPreference = 'Stop'

$sourcePath = (Resolve-Path $Source).Path.TrimEnd('\','/')
$outPath = (Resolve-Path (Split-Path -Parent $Out)).Path + '\\' + (Split-Path -Leaf $Out)

if (Test-Path $outPath) {
  Remove-Item $outPath -Force
}

Add-Type -AssemblyName System.IO.Compression

$fs = [System.IO.File]::Open($outPath, [System.IO.FileMode]::CreateNew)
$zip = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create, $false)

try {
  Get-ChildItem $sourcePath -Recurse -File | ForEach-Object {
    $full = $_.FullName

    # Compute a stable relative path without any leading './'
    $rel = $full.Substring($sourcePath.Length).TrimStart('\','/')
    if ([string]::IsNullOrWhiteSpace($rel)) {
      return
    }

    $entryName = $rel -replace '\\','/'

    $entry = $zip.CreateEntry($entryName, [System.IO.Compression.CompressionLevel]::Optimal)
    $inStream = [System.IO.File]::OpenRead($full)
    $outStream = $entry.Open()
    try {
      $inStream.CopyTo($outStream)
    }
    finally {
      $outStream.Dispose()
      $inStream.Dispose()
    }
  }
}
finally {
  $zip.Dispose()
  $fs.Dispose()
}

Get-Item $outPath | Select-Object Name, Length, FullName
