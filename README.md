# Claude Desktop Profiles (Windows)

Run several Claude Desktop accounts side by side on Windows: personal, work, a second personal, and so on.
Each one gets its own login, its own badge icon, and its **own taskbar button** that you can pin.

Your existing Claude Desktop install and login are never touched. It stays your "main" profile.

## How it works

- **Separate logins:** Claude Desktop is an Electron app. Starting it with `--user-data-dir=<folder>` gives it a fully separate profile: login, settings and sessions.
- **Separate taskbar buttons:** Windows groups taskbar buttons by *AppUserModelID*. `launch.ps1` starts Claude, then runs hidden in the background and sets a per-profile ID on that profile's windows (`SHGetPropertyStoreForWindow`). The shortcut carries the same ID, so a pinned shortcut and its windows share one button. The helper exits when Claude closes.
- **Badge icons:** Claude's logo with a coloured letter badge, like Chrome profiles.

## Option A: tray app (recommended)

A small Windows app: a tray icon with all your accounts, plus a window to add, open and manage them.

```powershell
git clone https://github.com/Akage1234/claude-desktop-profiles
cd claude-desktop-profiles
.\build.cmd              # compiles ClaudeProfiles.exe with the C# compiler built into Windows
.\ClaudeProfiles.exe
```

- **+ Add account:** pick a name, badge letter and colour, then click *Add & Open* and sign in. Desktop and Start Menu shortcuts are created too, ready to pin.
- **Tray icon:** left-click opens the window. Right-click lists every account (• means running) for one-click switching.
- **Stop:** fully quits a running account. Claude's own close button only hides it to the tray.
- **Manage ▾:** edit the display name and badge, re-create shortcuts, open the data folder, or delete the profile (this asks first).
- **Main** (your normal install) can get a display name and badge too. With a badge it gets its own taskbar button. Pin it via *Manage → Create shortcuts*.
- **Start with Windows:** while the app runs, it keeps every profile on its own taskbar button, however Claude was opened.
- Profiles made with the PowerShell scripts, or earlier versions, are picked up automatically.

App settings and icons are stored in `%LOCALAPPDATA%\ClaudeProfilesApp`. Profile data is in `Claude-<Name>` folders (see the Store note below).

## Option B: PowerShell only

Requirements: Windows 10/11 with Claude Desktop installed (Microsoft Store or the classic installer). No admin rights are needed.

```powershell
git clone https://github.com/Akage1234/claude-desktop-profiles
cd claude-desktop-profiles
.\new-profile.ps1 -Name Work -Letter W -Color "#16A34A"
```

This creates `Claude Work` shortcuts on the Desktop and in the Start Menu. Open it, sign in, then right-click the shortcut → *Show more options* → **Pin to taskbar**.

| Parameter | Default | |
|---|---|---|
| `-Name` | (required) | Letters, digits, `-` and `_` only. The data folder will be `%LOCALAPPDATA%\Claude-<Name>` |
| `-Letter` | First letter of `-Name` | Text in the badge |
| `-Color` | `#2563EB` | Badge colour |
| `-Force` | off | Overwrite existing shortcuts |

If PowerShell blocks the script, run it once with:
`powershell -ExecutionPolicy Bypass -File .\new-profile.ps1 -Name Work`

Keep this folder where it is after setup, because the shortcuts point into it.

## Notes and limits

- **Open profiles from their shortcut.** A profile opened any other way, for example by a sign-in link, shares the main button until you click its shortcut once.
- **Sign-in links:** the browser sign-in returns through a `claude://` link, which Windows sends to Claude. This normally reaches the window you're signing into. If it lands in the wrong window, use the email-code sign-in.
- **Store installs** virtualize the data folder, so it actually lives under `%LOCALAPPDATA%\Packages\Claude_*\LocalCache\Local\Claude-<Name>`.
- Startup, background services and Cowork's VM belong to the main install and are shared.

## Removing a profile

Delete its two `Claude <Name>.lnk` shortcuts and `icons\<Name>.ico`. To also delete its login and data, remove the `Claude-<Name>` folder (see the Store note above for where it is).

## Files

| File | |
|---|---|
| `ClaudeProfiles.cs` | The tray app (option A) |
| `build.cmd` | Builds `ClaudeProfiles.exe` |
| `make-icon.ps1` | Makes the app icon from your local Claude install at build time, so no Anthropic artwork is stored here |
| `new-profile.ps1` | Creates a profile's icon and shortcuts |
| `launch.ps1` | What the shortcut runs: starts Claude and tags its windows |
| `AppId.cs` | Small Win32 interop that sets AppUserModelIDs on windows and shortcuts |
| `common.ps1` | Finds the Claude install |

Inspired by [Leonxlnx/claude-desktop-multi-account](https://github.com/Leonxlnx/claude-desktop-multi-account). Not affiliated with Anthropic.

## License

MIT
