param(
    [string]$ProjectPath = (Join-Path $PSScriptRoot "..\Clipman.csproj"),
    [string]$Configuration = "Release",
    [string[]]$RuntimeIdentifiers = @("win-x64", "win-arm64"),
    [bool]$SelfContained = $true,
    [bool]$WindowsAppSDKSelfContained = $true,
    [bool]$PublishSingleFile = $false,
    [bool]$PublishTrimmed = $false,
    [string]$ArtifactsRoot = "artifacts\zip"
)

$ErrorActionPreference = "Stop"
$commonScriptPath = Join-Path $PSScriptRoot "common-build.ps1"
if (-not (Test-Path $commonScriptPath)) {
    throw "Common build helper not found: $commonScriptPath"
}
. $commonScriptPath

$projectFullPath = (Resolve-Path $ProjectPath).Path
$repoRoot = Split-Path -Parent $projectFullPath
$artifactsFullPath = Resolve-PathFromRepoRoot -RepoRoot $repoRoot -PathValue $ArtifactsRoot
$dotnetExe = Get-DotnetPath

Invoke-Step "Preparing artifacts directory: $artifactsFullPath" {
    New-Item -ItemType Directory -Force -Path $artifactsFullPath | Out-Null
}

$zipOutputs = @()

foreach ($rid in $RuntimeIdentifiers) {
    $publishDir = Join-Path $artifactsFullPath "publish\$rid"
    $zipName = "Clipman-$rid-$Configuration.zip"
    $zipPath = Join-Path $artifactsFullPath $zipName

    Invoke-Step "Publishing $rid" {
        if (Test-Path $publishDir) {
            Remove-Item -Recurse -Force $publishDir
        }

        $publishArgs = @(
            "publish", $projectFullPath,
            "-c", $Configuration,
            "-r", $rid,
            "-o", $publishDir,
            "-p:SelfContained=$($SelfContained.ToString().ToLowerInvariant())",
            "-p:WindowsAppSDKSelfContained=$($WindowsAppSDKSelfContained.ToString().ToLowerInvariant())",
            "-p:PublishSingleFile=$($PublishSingleFile.ToString().ToLowerInvariant())",
            "-p:PublishTrimmed=$($PublishTrimmed.ToString().ToLowerInvariant())",
            "-p:EnableMsixTooling=true",
            "-p:IncludeNativeLibrariesForSelfExtract=true"
        )

        & $dotnetExe @publishArgs
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed for $rid."
        }
    }

    Invoke-Step "Creating zip for ${rid}: $zipPath" {
        if (Test-Path $zipPath) {
            Remove-Item -Force $zipPath
        }

        Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -CompressionLevel Optimal
        if (-not (Test-Path $zipPath)) {
            throw "Failed to create zip: $zipPath"
        }
    }

    $zipOutputs += $zipPath
}

Write-Host ""
Write-Host "Done. ZIP artifacts:"
$zipOutputs | ForEach-Object { Write-Host " - $_" }
