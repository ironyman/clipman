param(
    [string]$ProjectPath = (Join-Path $PSScriptRoot "..\Clipman.csproj"),
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [string]$Version = "1.0.0",
    [string]$ProductName = "Clipman",
    [string]$Manufacturer = "Clipman",
    [string]$UpgradeCode = "4D4A4DC8-38E0-4C85-A0E1-2A0E2058A8A3",
    [string]$ArtifactsRoot = "artifacts\wix",
    [switch]$NoPublish
)

$ErrorActionPreference = "Stop"
$commonScriptPath = Join-Path $PSScriptRoot "common-build.ps1"
if (-not (Test-Path $commonScriptPath)) {
    throw "Common build helper not found: $commonScriptPath"
}
. $commonScriptPath

function Convert-RuntimeToArch {
    param([string]$Rid)
    switch ($Rid) {
        "win-x64" { return "x64" }
        "win-arm64" { return "arm64" }
        "win-x86" { return "x86" }
        default { throw "Unsupported RuntimeIdentifier '$Rid'. Use win-x86, win-x64, or win-arm64." }
    }
}

function Escape-Xml {
    param([string]$Text)
    return [System.Security.SecurityElement]::Escape($Text)
}

$projectFullPath = (Resolve-Path $ProjectPath).Path
$repoRoot = Split-Path -Parent $projectFullPath
$artifactsFullPath = Resolve-PathFromRepoRoot -RepoRoot $repoRoot -PathValue $ArtifactsRoot
$publishDir = Join-Path $artifactsFullPath "publish\$RuntimeIdentifier"
$installerDir = Join-Path $artifactsFullPath "installer"
$wxsPath = Join-Path $artifactsFullPath "Clipman.Installer.wxs"
$arch = Convert-RuntimeToArch -Rid $RuntimeIdentifier
$programFilesDirectoryId = if ($arch -eq "x86") { "ProgramFilesFolder" } else { "ProgramFiles64Folder" }
$msiName = "$ProductName-$Version-$RuntimeIdentifier.msi"
$msiPath = Join-Path $installerDir $msiName

$dotnetExe = Get-DotnetPath
$wixExe = Get-WixPath

Invoke-Step "Preparing artifacts directory: $artifactsFullPath" {
    New-Item -ItemType Directory -Force -Path $artifactsFullPath | Out-Null
    New-Item -ItemType Directory -Force -Path $installerDir | Out-Null
}

if (-not $NoPublish.IsPresent) {
    Invoke-Step "Publishing $RuntimeIdentifier for installer" {
        if (Test-Path $publishDir) {
            Remove-Item -Recurse -Force $publishDir
        }

        $publishArgs = @(
            "publish", $projectFullPath,
            "-c", $Configuration,
            "-r", $RuntimeIdentifier,
            "-o", $publishDir,
            "-p:SelfContained=true",
            "-p:WindowsAppSDKSelfContained=true",
            "-p:PublishSingleFile=false",
            "-p:PublishTrimmed=false",
            "-p:EnableMsixTooling=true",
            "-p:IncludeNativeLibrariesForSelfExtract=true"
        )

        & $dotnetExe @publishArgs
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed for $RuntimeIdentifier."
        }
    }
}

if (-not (Test-Path $publishDir)) {
    throw "Publish directory not found: $publishDir"
}

$wixSource = @"
<?xml version="1.0" encoding="utf-8"?>
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Package
    Name="$(Escape-Xml $ProductName)"
    Manufacturer="$(Escape-Xml $Manufacturer)"
    Version="$(Escape-Xml $Version)"
    UpgradeCode="$(Escape-Xml $UpgradeCode)"
    Scope="perMachine">
    <MajorUpgrade DowngradeErrorMessage="A newer version of $(Escape-Xml $ProductName) is already installed." />
    <MediaTemplate EmbedCab="yes" />
    <StandardDirectory Id="$programFilesDirectoryId">
      <Directory Id="INSTALLFOLDER" Name="$(Escape-Xml $ProductName)" />
    </StandardDirectory>
    <Feature Id="MainFeature" Title="$(Escape-Xml $ProductName)" Level="1">
      <ComponentGroupRef Id="AppFiles" />
    </Feature>
  </Package>
  <Fragment>
    <ComponentGroup Id="AppFiles" Directory="INSTALLFOLDER">
      <Files Include="`$(var.PublishDir)\**" />
    </ComponentGroup>
  </Fragment>
</Wix>
"@

Invoke-Step "Writing WiX source: $wxsPath" {
    Set-Content -Path $wxsPath -Value $wixSource -Encoding UTF8
}

Invoke-Step "Building MSI: $msiPath" {
    if (Test-Path $msiPath) {
        Remove-Item -Force $msiPath
    }

    $wixArgs = @(
        "build", $wxsPath,
        "-arch", $arch,
        "-ext", "WixToolset.Heat",
        "-d", "PublishDir=$publishDir",
        "-o", $msiPath
    )

    & $wixExe @wixArgs
    if ($LASTEXITCODE -ne 0) {
        throw "wix build failed. Ensure WiX heat extension is installed: wix extension add WixToolset.Heat"
    }
}

Write-Host ""
Write-Host "Done. MSI artifact:"
Write-Host " - $msiPath"
