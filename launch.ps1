# Launches Claude Desktop with a separate profile and gives its windows their own
# taskbar identity (AppUserModelID), so each profile gets its own pinnable button.
# Usage: launch.ps1 -Name Work      (profile data: %LOCALAPPDATA%\Claude-<Name>)
param([Parameter(Mandatory)][string]$Name)

. "$PSScriptRoot\common.ps1"
$dir  = "$env:LOCALAPPDATA\Claude-$Name"
$id   = "ClaudeProfile.$Name"
$icon = "$PSScriptRoot\icons\$Name.ico"
$self = "conhost.exe --headless powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Name $Name"

Add-Type -Path "$PSScriptRoot\AppId.cs"

Start-Process (Get-ClaudeExe) -ArgumentList "--user-data-dir=`"$dir`""

# One watcher per profile; a second click just focuses Claude (above) and exits.
$mutex = New-Object Threading.Mutex($false, "Local\ClaudeProfileWatch.$Name")
if (-not $mutex.WaitOne(0)) { exit }

# Match by folder name: Store installs virtualize the path, but the name survives.
$pattern = "*user-data-dir=*\Claude-$Name`"*"
$deadline = (Get-Date).AddSeconds(60)
while ($true) {
    $pids = @(Get-CimInstance Win32_Process -Filter "Name='claude.exe'" |
        Where-Object { $_.CommandLine -like $pattern } | ForEach-Object { [uint32]$_.ProcessId })
    if ($pids.Count) { $deadline = $null; [AppId]::TagWindows($pids, $id, $self, "$icon,0", "Claude $Name") }
    elseif (-not $deadline -or (Get-Date) -gt $deadline) { break }  # Claude closed (or never started)
    Start-Sleep -Milliseconds 1500
}
