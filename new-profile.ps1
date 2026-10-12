# Creates a Claude Desktop profile: badge icon + Desktop and Start Menu shortcuts.
# Example: .\new-profile.ps1 -Name Work -Letter W -Color "#16A34A"
param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_-]+$')][string]$Name,
    [string]$Letter = $Name.Substring(0, 1).ToUpper(),
    [string]$Color = "#2563EB",
    [switch]$Force
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\common.ps1"
Add-Type -AssemblyName System.Drawing
Add-Type -Path "$PSScriptRoot\AppId.cs"

$exe = Get-ClaudeExe
$targets = @("$([Environment]::GetFolderPath('Desktop'))\Claude $Name.lnk",
             "$([Environment]::GetFolderPath('Programs'))\Claude $Name.lnk")
if (-not $Force) { $targets | Where-Object { Test-Path $_ } | ForEach-Object { throw "$_ already exists (use -Force to overwrite)" } }

# --- Icon: Claude logo with a Chrome-style letter badge in the bottom-right ---
$n = 256
$bmp = New-Object Drawing.Bitmap $n, $n
$g = [Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.TextRenderingHint = 'AntiAliasGridFit'

$pkg = Get-AppxPackage -Name Claude -ErrorAction SilentlyContinue
$asset = if ($pkg) { "$($pkg.InstallLocation)\assets\Square310x310Logo.scale-400.png" }
if ($asset -and (Test-Path $asset)) {
    $src = [Drawing.Image]::FromFile($asset)
    $crop = $src.Width * 0.52; $off = ($src.Width - $crop) / 2   # asset has wide padding
    $g.DrawImage($src, (New-Object Drawing.RectangleF 0, 0, $n, $n), (New-Object Drawing.RectangleF $off, $off, $crop, $crop), [Drawing.GraphicsUnit]::Pixel)
    $src.Dispose()
} else {
    $g.DrawImage([Drawing.Icon]::ExtractAssociatedIcon($exe).ToBitmap(), 0, 0, $n, $n)
}

$d = 104; $x = $n - $d - 4; $y = $x
$g.FillEllipse([Drawing.Brushes]::White, $x - 7, $y - 7, $d + 14, $d + 14)
$g.FillEllipse((New-Object Drawing.SolidBrush ([Drawing.ColorTranslator]::FromHtml($Color))), $x, $y, $d, $d)
$font = New-Object Drawing.Font 'Segoe UI', 62, ([Drawing.FontStyle]::Bold), ([Drawing.GraphicsUnit]::Pixel)
$fmt = New-Object Drawing.StringFormat; $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
$g.DrawString($Letter, $font, [Drawing.Brushes]::White, (New-Object Drawing.RectangleF $x, ($y + 4), $d, $d), $fmt)
$g.Dispose()

# .ico holding one PNG frame (supported since Windows Vista)
$ms = New-Object IO.MemoryStream; $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $png = $ms.ToArray(); $bmp.Dispose()
New-Item -ItemType Directory -Force "$PSScriptRoot\icons" | Out-Null
$ico = "$PSScriptRoot\icons\$Name.ico"
$w = New-Object IO.BinaryWriter ([IO.File]::Create($ico))
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]1)                 # header: icon, 1 image
$w.Write([byte]0); $w.Write([byte]0); $w.Write([byte]0); $w.Write([byte]0)    # 256x256, no palette
$w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$png.Length); $w.Write([UInt32]22)
$w.Write($png); $w.Close()

# --- Shortcuts: run the launcher hidden, carry the profile's taskbar ID ---
$sh = New-Object -ComObject WScript.Shell
foreach ($lnk in $targets) {
    $s = $sh.CreateShortcut($lnk)
    $s.TargetPath = "$env:WINDIR\System32\conhost.exe"
    $s.Arguments = "--headless powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$PSScriptRoot\launch.ps1`" -Name $Name"
    $s.IconLocation = "$ico,0"
    $s.WindowStyle = 7
    $s.Description = "Claude Desktop ($Name profile)"
    $s.Save()
    [AppId]::TagShortcut($lnk, "ClaudeProfile.$Name")
    "Created $lnk"
}
"Profile data will live in %LOCALAPPDATA%\Claude-$Name. Open it from the shortcut and sign in."
