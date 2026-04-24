Set-StrictMode -Version Latest

function Invoke-Step {
    param(
        [Parameter(Mandatory = $true)][string]$Message,
        [Parameter(Mandatory = $true)][scriptblock]$Action
    )

    Write-Host "==> $Message"
    & $Action
}

function Resolve-PathFromRepoRoot {
    param(
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$PathValue
    )

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $PathValue))
}

function Resolve-ToolPath {
    param(
        [Parameter(Mandatory = $true)][string]$ToolName,
        [Parameter(Mandatory = $true)][string]$CommandName,
        [string[]]$CandidatePaths = @()
    )

    $command = Get-Command $CommandName -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) {
        if ($command.Source) {
            return $command.Source
        }

        if ($command.Path) {
            return $command.Path
        }
    }

    foreach ($candidate in $CandidatePaths) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            continue
        }

        $expanded = [Environment]::ExpandEnvironmentVariables($candidate)
        if (Test-Path $expanded) {
            return (Resolve-Path $expanded).Path
        }
    }

    $searched = @($CandidatePaths | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join "; "
    if ([string]::IsNullOrWhiteSpace($searched)) {
        throw "$ToolName was not found in PATH."
    }

    throw "$ToolName was not found in PATH or known locations. Searched: $searched"
}

function Get-DotnetPath {
    $dotnetRoot = [Environment]::GetEnvironmentVariable("DOTNET_ROOT")
    $dotnetRootX86 = [Environment]::GetEnvironmentVariable("DOTNET_ROOT(x86)")
    $candidates = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($dotnetRoot)) {
        $candidates.Add((Join-Path $dotnetRoot "dotnet.exe"))
    }
    if (-not [string]::IsNullOrWhiteSpace($dotnetRootX86)) {
        $candidates.Add((Join-Path $dotnetRootX86 "dotnet.exe"))
    }
    $candidates.Add("$env:ProgramFiles\dotnet\dotnet.exe")
    $candidates.Add("${env:ProgramFiles(x86)}\dotnet\dotnet.exe")
    $candidates.Add("$env:LocalAppData\Microsoft\dotnet\dotnet.exe")

    return Resolve-ToolPath -ToolName "dotnet SDK" -CommandName "dotnet" -CandidatePaths $candidates
}

function Get-WixPath {
    $candidates = @(
        "$env:USERPROFILE\.dotnet\tools\wix.exe"
    )

    return Resolve-ToolPath -ToolName "WiX CLI (wix)" -CommandName "wix" -CandidatePaths $candidates
}

function Get-VsWherePath {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    )

    return Resolve-ToolPath -ToolName "vswhere.exe" -CommandName "vswhere" -CandidatePaths $candidates
}
