param(
    [string]$Glyph = "E8C8",
    [int]$Size = 256,
    [string]$OutputDirectory = "..\\Assets",
    [string]$BaseName = "clipman",
    [string]$ForegroundHex = "#121212"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $PSCommandPath
$outDir = Resolve-Path (Join-Path $scriptDir $OutputDirectory) -ErrorAction SilentlyContinue
if (-not $outDir) {
    $target = Join-Path $scriptDir $OutputDirectory
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    $outDir = Resolve-Path $target
}

$pngPath = Join-Path $outDir "$BaseName.png"
$icoPath = Join-Path $outDir "$BaseName.ico"

Add-Type -AssemblyName System.Drawing

$bmp = New-Object System.Drawing.Bitmap($Size, $Size)
$gfx = [System.Drawing.Graphics]::FromImage($bmp)
$gfx.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$gfx.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
$gfx.Clear([System.Drawing.Color]::Transparent)

$fontSize = [int]($Size * 0.70)
$font = New-Object System.Drawing.Font("Segoe MDL2 Assets", $fontSize, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$hex = $ForegroundHex.Trim()
if ($hex.StartsWith("#")) {
    $hex = $hex.Substring(1)
}

if ($hex.Length -ne 6) {
    throw "ForegroundHex must be in #RRGGBB format."
}

$r = [Convert]::ToInt32($hex.Substring(0, 2), 16)
$g = [Convert]::ToInt32($hex.Substring(2, 2), 16)
$b = [Convert]::ToInt32($hex.Substring(4, 2), 16)
$brush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, $r, $g, $b))
$fmt = New-Object System.Drawing.StringFormat
$fmt.Alignment = [System.Drawing.StringAlignment]::Center
$fmt.LineAlignment = [System.Drawing.StringAlignment]::Center

$glyphChar = [char]([convert]::ToInt32($Glyph, 16))
$rect = New-Object System.Drawing.RectangleF(0, 0, $Size, $Size)
$gfx.DrawString($glyphChar, $font, $brush, $rect, $fmt)

$bmp.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
$icon = [System.Drawing.Icon]::FromHandle($bmp.GetHicon())
$fs = [System.IO.File]::Create($icoPath)
$icon.Save($fs)
$fs.Close()

$icon.Dispose()
$fmt.Dispose()
$brush.Dispose()
$font.Dispose()
$gfx.Dispose()
$bmp.Dispose()

Write-Host "Generated:"
Write-Host "  $pngPath"
Write-Host "  $icoPath"
