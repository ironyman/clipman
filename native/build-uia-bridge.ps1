param(
    [string]$Architecture = "x64",
    [string]$VcVarsPath = "",
    [string]$WindowsKitLibRoot = "",
    [string]$VsWherePath = ""
)

$ErrorActionPreference = "Stop"

$commonScriptPath = Join-Path $PSScriptRoot "..\scripts\common-build.ps1"
if (-not (Test-Path $commonScriptPath)) {
    throw "Common build helper not found: $commonScriptPath"
}
. $commonScriptPath

function Get-VsWhereExe {
    param([string]$ExplicitPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        if (-not (Test-Path $ExplicitPath)) {
            throw "Specified VsWherePath was not found: $ExplicitPath"
        }

        return (Resolve-Path $ExplicitPath).Path
    }

    return Get-VsWherePath
}

function Get-VsInstallationPath {
    param([string]$VsWhereExe)

    $path = & $VsWhereExe -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    $path = ($path | Select-Object -First 1).Trim()
    if ([string]::IsNullOrWhiteSpace($path)) {
        throw "No Visual Studio installation with C++ tools was found."
    }

    return $path
}

function Get-VcVars64Path {
    param(
        [string]$ExplicitPath,
        [string]$VsInstallPath
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        if (-not (Test-Path $ExplicitPath)) {
            throw "Specified VcVarsPath was not found: $ExplicitPath"
        }

        return (Resolve-Path $ExplicitPath).Path
    }

    $candidate = Join-Path $VsInstallPath "VC\Auxiliary\Build\vcvars64.bat"
    if (Test-Path $candidate) {
        return $candidate
    }

    throw "vcvars64.bat was not found under Visual Studio installation: $VsInstallPath"
}

function Get-WindowsKitLibDirectory {
    param(
        [string]$ExplicitRoot,
        [string]$Arch
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitRoot)) {
        $umPath = Join-Path $ExplicitRoot "um\$Arch"
        if (-not (Test-Path $umPath)) {
            throw "Specified WindowsKitLibRoot does not contain um\${Arch}: $ExplicitRoot"
        }

        return (Resolve-Path $ExplicitRoot).Path
    }

    $kitsRoot = ""
    try {
        $kitsRoot = (Get-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots" -Name KitsRoot10).KitsRoot10
    }
    catch {
    }

    if ([string]::IsNullOrWhiteSpace($kitsRoot)) {
        $kitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\"
    }

    $libRoot = Join-Path $kitsRoot "Lib"
    if (-not (Test-Path $libRoot)) {
        throw "Windows SDK Lib directory not found: $libRoot"
    }

    $versionDir = Get-ChildItem -Path $libRoot -Directory |
        Where-Object { Test-Path (Join-Path $_.FullName "um\$Arch") } |
        Sort-Object { [version]$_.Name } -Descending |
        Select-Object -First 1

    if (-not $versionDir) {
        throw "No Windows SDK version with um\$Arch found under: $libRoot"
    }

    return $versionDir.FullName
}

function Get-VcToolsLibDirectory {
    param(
        [string]$VsInstallPath,
        [string]$Arch
    )

    $msvcRoot = Join-Path $VsInstallPath "VC\Tools\MSVC"
    if (-not (Test-Path $msvcRoot)) {
        throw "MSVC tools directory not found: $msvcRoot"
    }

    $toolDir = Get-ChildItem -Path $msvcRoot -Directory |
        Where-Object { Test-Path (Join-Path $_.FullName "lib\$Arch") } |
        Sort-Object { [version]$_.Name } -Descending |
        Select-Object -First 1

    if (-not $toolDir) {
        throw "No MSVC toolset with lib\$Arch found under: $msvcRoot"
    }

    return Join-Path $toolDir.FullName "lib\$Arch"
}

if ($Architecture -ne "x64") {
    throw "Unsupported Architecture '$Architecture'. This script currently supports only x64."
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sources = @(
    (Join-Path $scriptDir "clipman_uia_bridge.cpp"),
    (Join-Path $scriptDir "clipman_caret_locator.cpp")
)
$outputDir = Join-Path $scriptDir "bin"
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

$vsWhereExe = Get-VsWhereExe -ExplicitPath $VsWherePath
$vsInstallPath = Get-VsInstallationPath -VsWhereExe $vsWhereExe
$vsVcVars = Get-VcVars64Path -ExplicitPath $VcVarsPath -VsInstallPath $vsInstallPath
$windowsKitLib = Get-WindowsKitLibDirectory -ExplicitRoot $WindowsKitLibRoot -Arch $Architecture
$vcToolsLib = Get-VcToolsLibDirectory -VsInstallPath $vsInstallPath -Arch $Architecture

Write-Host "Using vcvars: $vsVcVars"
Write-Host "Using Windows SDK lib: $windowsKitLib"
Write-Host "Using MSVC lib: $vcToolsLib"

$outputDll = Join-Path $outputDir "clipman_uia_bridge.dll"
$quotedSources = ($sources | ForEach-Object { "`"$_`"" }) -join " "
$cmd = "call `"$vsVcVars`" && cl /nologo /LD /MT /EHsc /std:c++17 $quotedSources /link /OUT:`"$outputDll`" /LIBPATH:`"$windowsKitLib\um\$Architecture`" /LIBPATH:`"$vcToolsLib`" Ole32.lib OleAut32.lib Oleacc.lib User32.lib Uiautomationcore.lib"
cmd /c $cmd

if (-not (Test-Path $outputDll)) {
    throw "Native bridge build failed: $outputDll not found."
}

Write-Host "Built $outputDll"
