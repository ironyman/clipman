$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptDir
$sources = @(
    (Join-Path $scriptDir "clipman_uia_bridge.cpp"),
    (Join-Path $scriptDir "clipman_caret_locator.cpp")
)
$outputDir = Join-Path $scriptDir "bin"
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

$vsVcVars = "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat"
$windowsKitLib = "C:\Program Files (x86)\Windows Kits\10\Lib\10.0.26100.0"
$vcTools = "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Tools\MSVC\14.44.35207"

$outputDll = Join-Path $outputDir "clipman_uia_bridge.dll"
$quotedSources = ($sources | ForEach-Object { "`"$_`"" }) -join " "
$cmd = "call `"$vsVcVars`" && cl /nologo /LD /MT /EHsc /std:c++17 $quotedSources /link /OUT:`"$outputDll`" /LIBPATH:`"$windowsKitLib\um\x64`" /LIBPATH:`"$vcTools\lib\x64`" Ole32.lib OleAut32.lib Oleacc.lib User32.lib Uiautomationcore.lib"
cmd /c $cmd

if (-not (Test-Path $outputDll)) {
    throw "Native bridge build failed: $outputDll not found."
}

Write-Host "Built $outputDll"
