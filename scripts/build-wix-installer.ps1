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

function Get-PathHash {
    param([Parameter(Mandatory = $true)][string]$Text)

    $sha = [System.Security.Cryptography.SHA1]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
        $hash = $sha.ComputeHash($bytes)
        return ([BitConverter]::ToString($hash) -replace "-", "").Substring(0, 12)
    }
    finally {
        $sha.Dispose()
    }
}

function Get-DirectoryId {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$RelativeDirectory)

    if ([string]::IsNullOrWhiteSpace($RelativeDirectory) -or $RelativeDirectory -eq ".") {
        return "INSTALLFOLDER"
    }

    return "DIR_" + (Get-PathHash -Text $RelativeDirectory)
}

function Get-RelativePathCompat {
    param(
        [Parameter(Mandatory = $true)][string]$BasePath,
        [Parameter(Mandatory = $true)][string]$TargetPath
    )

    $baseFull = [System.IO.Path]::GetFullPath($BasePath)
    $targetFull = [System.IO.Path]::GetFullPath($TargetPath)
    $baseUri = New-Object System.Uri(($baseFull.TrimEnd('\') + "\"))
    $targetUri = New-Object System.Uri($targetFull)
    $relative = $baseUri.MakeRelativeUri($targetUri).ToString()
    return [System.Uri]::UnescapeDataString($relative).Replace('/', '\')
}

function Build-DirectoryTreeXml {
    param(
        [Parameter(Mandatory = $true)][hashtable]$ChildDirsByParent,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$ParentDir,
        [Parameter(Mandatory = $true)][int]$IndentLevel
    )

    if (-not $ChildDirsByParent.ContainsKey($ParentDir)) {
        return ""
    }

    $indent = "  " * $IndentLevel
    $lines = New-Object System.Collections.ArrayList
    foreach ($child in ($ChildDirsByParent[$ParentDir] | Sort-Object)) {
        $name = Split-Path -Path $child -Leaf
        $dirId = Get-DirectoryId -RelativeDirectory $child
        [void]$lines.Add("$indent<Directory Id=""$dirId"" Name=""$(Escape-Xml $name)"">")
        $inner = Build-DirectoryTreeXml -ChildDirsByParent $ChildDirsByParent -ParentDir $child -IndentLevel ($IndentLevel + 1)
        if (-not [string]::IsNullOrWhiteSpace($inner)) {
            [void]$lines.Add($inner)
        }
        [void]$lines.Add("$indent</Directory>")
    }

    return ($lines -join [Environment]::NewLine)
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
$wixExe = Ensure-WixInstalled

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

$publishFiles = Get-ChildItem -Path $publishDir -File -Recurse | Sort-Object FullName
if ($publishFiles.Count -eq 0) {
    throw "No files found in publish directory: $publishDir"
}

$allDirs = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
$allDirs.Add("") | Out-Null
$filesByDir = @{}

foreach ($file in $publishFiles) {
    $relativePath = Get-RelativePathCompat -BasePath $publishDir -TargetPath $file.FullName
    $relativeDir = [System.IO.Path]::GetDirectoryName($relativePath)
    if ([string]::IsNullOrWhiteSpace($relativeDir)) {
        $relativeDir = ""
    }

    $allDirs.Add($relativeDir) | Out-Null

    $parts = $relativeDir -split '[\\/]'
    $cursor = ""
    foreach ($part in $parts) {
        if ([string]::IsNullOrWhiteSpace($part)) {
            continue
        }

        $cursor = if ([string]::IsNullOrEmpty($cursor)) { $part } else { "$cursor\$part" }
        $allDirs.Add($cursor) | Out-Null
    }

    if (-not $filesByDir.ContainsKey($relativeDir)) {
        $filesByDir[$relativeDir] = New-Object System.Collections.ArrayList
    }
    [void]$filesByDir[$relativeDir].Add($file.FullName)
}

$childDirsByParent = @{}
foreach ($dir in ($allDirs | Sort-Object)) {
    if ([string]::IsNullOrEmpty($dir)) {
        continue
    }

    $parent = [System.IO.Path]::GetDirectoryName($dir)
    if ([string]::IsNullOrWhiteSpace($parent)) {
        $parent = ""
    }

    if (-not $childDirsByParent.ContainsKey($parent)) {
        $childDirsByParent[$parent] = New-Object System.Collections.ArrayList
    }

    [void]$childDirsByParent[$parent].Add($dir)
}

$componentRefs = New-Object System.Collections.ArrayList
$directoryRefBlocks = New-Object System.Collections.ArrayList
$componentCounter = 0

foreach ($dir in ($allDirs | Sort-Object)) {
    if (-not $filesByDir.ContainsKey($dir)) {
        continue
    }

    $dirId = Get-DirectoryId -RelativeDirectory $dir
    $componentLines = New-Object System.Collections.ArrayList
    foreach ($filePath in ($filesByDir[$dir] | Sort-Object)) {
        $componentCounter++
        $componentId = "CMP_$componentCounter"
        $fileId = "FIL_$componentCounter"
        $escapedFilePath = Escape-Xml $filePath

        [void]$componentLines.Add("      <Component Id=""$componentId"" Guid=""*"">")
        [void]$componentLines.Add("        <File Id=""$fileId"" Source=""$escapedFilePath"" KeyPath=""yes"" />")
        [void]$componentLines.Add("      </Component>")
        [void]$componentRefs.Add("      <ComponentRef Id=""$componentId"" />")
    }

    $directoryRefBlock = @(
        "    <DirectoryRef Id=""$dirId"">"
        ($componentLines -join [Environment]::NewLine)
        "    </DirectoryRef>"
    ) -join [Environment]::NewLine
    [void]$directoryRefBlocks.Add($directoryRefBlock)
}

$directoryTreeXml = Build-DirectoryTreeXml -ChildDirsByParent $childDirsByParent -ParentDir "" -IndentLevel 3
$directoryRefsXml = ($directoryRefBlocks -join [Environment]::NewLine)
$componentRefsXml = ($componentRefs -join [Environment]::NewLine)

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
      <Directory Id="INSTALLFOLDER" Name="$(Escape-Xml $ProductName)">
${directoryTreeXml}
      </Directory>
    </StandardDirectory>
    <Feature Id="MainFeature" Title="$(Escape-Xml $ProductName)" Level="1">
      <ComponentGroupRef Id="AppFiles" />
    </Feature>
  </Package>
  <Fragment>
${directoryRefsXml}
  </Fragment>
  <Fragment>
    <ComponentGroup Id="AppFiles">
${componentRefsXml}
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
        "-o", $msiPath
    )

    & $wixExe @wixArgs
    if ($LASTEXITCODE -ne 0) {
        throw "wix build failed."
    }
}

Write-Host ""
Write-Host "Done. MSI artifact:"
Write-Host " - $msiPath"
