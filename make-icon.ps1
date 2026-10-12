# Builds app.ico: Claude's orange starburst on a white circle, taken from the local
# Claude Desktop install (so Anthropic's artwork is never stored in this repo).
param([string]$Out = "$PSScriptRoot\app.ico")
Add-Type -AssemblyName System.Drawing

$pkg = Get-AppxPackage -Name Claude -ErrorAction SilentlyContinue
$asset = if ($pkg) { "$($pkg.InstallLocation)\assets\Square310x310Logo.scale-400.png" }
$orange = [Drawing.ColorTranslator]::FromHtml('#D97757')

# The asset is a white starburst on an orange tile: turn "how white" into an alpha mask.
$mask = $null
if ($asset -and (Test-Path $asset)) {
    $src = [Drawing.Bitmap]::FromFile($asset)
    $crop = [int]($src.Width * 0.40); $off = [int](($src.Width - $crop) / 2)   # just the starburst
    $mask = New-Object Drawing.Bitmap 512, 512
    $g = [Drawing.Graphics]::FromImage($mask); $g.InterpolationMode = 'HighQualityBicubic'
    $g.DrawImage($src, (New-Object Drawing.Rectangle 0, 0, 512, 512), (New-Object Drawing.Rectangle $off, $off, $crop, $crop), [Drawing.GraphicsUnit]::Pixel)
    $g.Dispose(); $src.Dispose()
    $data = $mask.LockBits((New-Object Drawing.Rectangle 0, 0, 512, 512), 'ReadWrite', 'Format32bppArgb')
    $bytes = New-Object byte[] ($data.Stride * 512)
    [Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    for ($i = 0; $i -lt $bytes.Length; $i += 4) {   # BGRA
        $white = [Math]::Max(0, [Math]::Min(1, ($bytes[$i] - $orange.B) / (255.0 - $orange.B)))
        $bytes[$i + 3] = [byte]($white * $bytes[$i + 3])
        $bytes[$i] = $orange.B; $bytes[$i + 1] = $orange.G; $bytes[$i + 2] = $orange.R
    }
    [Runtime.InteropServices.Marshal]::Copy($bytes, 0, $data.Scan0, $bytes.Length)
    $mask.UnlockBits($data)
} else { Write-Warning "Claude (Store) not found; using a plain orange dot." }

function Draw([int]$n) {
    $b = New-Object Drawing.Bitmap $n, $n
    $g = [Drawing.Graphics]::FromImage($b); $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'
    $g.FillEllipse([Drawing.Brushes]::White, 0.5, 0.5, $n - 1, $n - 1)
    $g.DrawEllipse((New-Object Drawing.Pen ([Drawing.Color]::FromArgb(40, 0, 0, 0)), ([Math]::Max(1, $n / 64))), 0.5, 0.5, $n - 1.5, $n - 1.5)
    $m = $n * 0.14
    if ($mask) { $g.DrawImage($mask, $m, $m, $n - 2 * $m, $n - 2 * $m) }
    else { $g.FillEllipse((New-Object Drawing.SolidBrush $orange), $n * 0.3, $n * 0.3, $n * 0.4, $n * 0.4) }
    $g.Dispose(); $b
}

$sizes = 16, 24, 32, 48, 64, 256
$pngs = foreach ($s in $sizes) { $ms = New-Object IO.MemoryStream; (Draw $s).Save($ms, [Drawing.Imaging.ImageFormat]::Png); , $ms.ToArray() }
$w = New-Object IO.BinaryWriter ([IO.File]::Create($Out))
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i] % 256   # 0 means 256
    $w.Write([byte]$s); $w.Write([byte]$s); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$pngs[$i].Length); $w.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write([byte[]]$p) }
$w.Close()
(Draw 256).Save("$env:TEMP\claude-profiles-icon-preview.png")
