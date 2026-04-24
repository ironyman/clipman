param(
    [switch]$Quiet
)

$ErrorActionPreference = "Stop"

$commonScriptPath = Join-Path $PSScriptRoot "common-build.ps1"
if (-not (Test-Path $commonScriptPath)) {
    throw "Common build helper not found: $commonScriptPath"
}

. $commonScriptPath

function Add-PathEntry {
    param([Parameter(Mandatory = $true)][string]$Entry)

    $normalizedEntry = [System.IO.Path]::GetFullPath($Entry).TrimEnd('\')
    $existing = @($env:PATH -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $normalizedExisting = $existing | ForEach-Object { ([System.IO.Path]::GetFullPath($_).TrimEnd('\')) }

    if ($normalizedExisting -contains $normalizedEntry) {
        return
    }

    $env:PATH = "$Entry;$env:PATH"
}

$dotnetExe = Get-DotnetPath
$dotnetDir = Split-Path -Parent $dotnetExe
$env:DOTNET_ROOT = $dotnetDir
Add-PathEntry -Entry $dotnetDir

$wixExe = $null
try {
    $wixExe = Get-WixPath
    Add-PathEntry -Entry (Split-Path -Parent $wixExe)
}
catch {
}

if (-not $Quiet.IsPresent) {
    Write-Host "Clipman dev environment configured for this shell."
    Write-Host "DOTNET_ROOT = $env:DOTNET_ROOT"
    Write-Host "dotnet      = $dotnetExe"
    if ($wixExe) {
        Write-Host "wix         = $wixExe"
    }
}
