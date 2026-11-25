# Renders src/MkRgb.Desktop/Assets/mk-logo.xaml into mk-rgb.ico (all standard sizes) and a 512 px PNG.
# Run with Windows PowerShell (STA by default):  powershell -File scripts/make-icon.ps1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
$repo = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $repo 'src/MkRgb.Desktop/Assets'
$xaml = [IO.File]::ReadAllText((Join-Path $assets 'mk-logo.xaml'))

function Render([int]$size) {
    $logo = [Windows.Markup.XamlReader]::Parse($xaml)
    # Letters are unreadable below 32 px; small sizes show the flame alone, which stays crisp.
    if ($size -lt 32) { $logo.FindName('Letters').Visibility = 'Collapsed' }
    $logo.Width = $size; $logo.Height = $size
    $logo.Measure([Windows.Size]::new($size, $size))
    $logo.Arrange([Windows.Rect]::new(0, 0, $size, $size))
    $logo.UpdateLayout()
    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($logo)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new()
    $encoder.Save($stream)
    , $stream.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($size in $sizes) { , (Render $size) }
# ICO container with PNG-compressed entries (supported since Windows Vista).
$ico = [IO.MemoryStream]::new()
$writer = [IO.BinaryWriter]::new($ico)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $edge = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$edge); $writer.Write([byte]$edge); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write($image) }
$writer.Flush()
[IO.File]::WriteAllBytes((Join-Path $assets 'mk-rgb.ico'), $ico.ToArray())
[IO.File]::WriteAllBytes((Join-Path $repo 'docs/logo.png'), (Render 512))
Write-Output "Wrote $(Join-Path $assets 'mk-rgb.ico') and docs/logo.png"
