# Finds Claude Desktop: Microsoft Store install (execution alias) or the classic installer.
function Get-ClaudeExe {
    foreach ($p in "$env:LOCALAPPDATA\Microsoft\WindowsApps\claude-desktop.exe",
                   "$env:LOCALAPPDATA\AnthropicClaude\claude.exe") {
        if (Test-Path $p) { return $p }
    }
    throw "Claude Desktop not found. Install it from https://claude.ai/download"
}
