param(
  [string]$OutFile = ("clipman-debug-{0:yyyyMMdd-HHmmss}.etl" -f (Get-Date)),
  [string]$ProjectRoot = (Resolve-Path ".").Path
)

$ErrorActionPreference = 'Stop'

$symbolParts = @(
  (Join-Path $ProjectRoot "bin"),
  (Join-Path $ProjectRoot "obj"),
  "srv*C:\\Symbols*https://msdl.microsoft.com/download/symbols"
)

$env:_NT_SYMBOL_PATH = ($symbolParts -join ';')
Write-Host "_NT_SYMBOL_PATH=$env:_NT_SYMBOL_PATH"

cmd /c "wpr -cancel >nul 2>nul"
wpr -start "scripts\\wpr\\ClipmanDotnetDebug.wprp!ClipmanDebug" -filemode
Write-Host "WPR started. Repro the freeze, then run: wpr -stop $OutFile"
