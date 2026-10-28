# BackMaster for Windows

Blocks AI assistants and other configured websites on classroom PCs during scheduled
lessons (default: Wednesday 11:00–12:30 and Thursday 09:10–10:50, Europe/Sofia).

| Project | What it is |
|---|---|
| `src/BackMaster.Core` | Platform-independent logic: schedule, policy, domain/hosts handling, phrase hashing, IPC protocol, Bulgarian tray texts, health checks |
| `src/BackMaster.Platform` | Windows pieces shared by the service and tray: ACLs, hosts-file writer, named pipe, elevation |
| `src/BackMaster.Service` | `BackMaster` Windows service (LocalSystem). Applies the hosts-file block during lessons, answers the tray |
| `src/BackMaster.Tray` | Tray icon for students, hotkey → phrase dialog → settings window, elevated save helper |
| `installer` | WiX 5 MSI (pulled from NuGet; nothing to install) |

## How it fits together

- **Filtering**: during a lesson the service writes a marked section into
  `C:\Windows\System32\drivers\etc\hosts` that sends every listed domain (and `www.`) to
  `0.0.0.0` / `::`, then flushes the DNS cache. Outside lessons, or when the service stops,
  the section is removed. The section is re-checked every 5 s. The installer turns off
  DNS-over-HTTPS in Chrome, Edge, Brave and Firefox via machine policy, because DoH bypasses the hosts file.
- **Settings access**: the tray registers a global hotkey (default `Ctrl+Alt+Shift+B`). It opens
  a phrase dialog; the phrase is sent to the service, which checks it against a PBKDF2-SHA256
  hash (600 000 iterations, random salt) in `%ProgramData%\BackMaster\private\unlock.verifier`.
  Only SYSTEM and Administrators can read that folder. After 5 wrong guesses the service
  locks out for 30 s, doubling to 15 min. The tray only talks to a pipe server in session 0,
  so a student program squatting the pipe name never receives the phrase.
- **Saving**: the settings window cannot write anything itself. **Save** starts an elevated copy
  of the tray (UAC prompt); it checks the SHA-256 of the handed-over settings, validates them,
  and writes `%ProgramData%\BackMaster\policy.json` (Users: read-only). The service reloads it within 5 s.
- **First phrase**: Start menu → **BackMaster Settings (Administrator)** → *Достъп* tab.

## Build and test (Windows, .NET 8 SDK or later)

Windows blocks `.ps1` scripts by default. Allow them for the current PowerShell window only:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
dotnet test BackMaster.sln                       # all unit + Windows integration tests
.\build.ps1                                      # tests, publish, artifacts\BackMaster.msi
.\build.ps1 -SkipTests                           # faster rebuild of the MSI
```

Run the service in a console without touching the real machine (no admin needed):

```powershell
dotnet run --project src\BackMaster.Service -- --data-dir $env:TEMP\bm --hosts $env:TEMP\bm-hosts --pipe BackMaster.Dev --no-harden
```

## Classroom PC checklist

Install and verify (elevated PowerShell):

```powershell
msiexec /i artifacts\BackMaster.msi /l*v $env:TEMP\backmaster-install.log
Get-Service BackMaster                                  # Running, Automatic
Get-Content C:\ProgramData\BackMaster\policy.json
icacls C:\ProgramData\BackMaster; icacls C:\ProgramData\BackMaster\private
Get-WinEvent -LogName Application -MaxEvents 50 | ? ProviderName -eq BackMaster | Select -First 10 | fl TimeCreated,Message
```

1. Start menu → **BackMaster Settings (Administrator)** → approve UAC → *Достъп* → set a phrase → Save.
2. Sign out and in as a student (or start **BackMaster** from the Start menu): grey/green tray icon with a tooltip.
3. Settings → *График* → mode **Винаги включено** → Save (UAC). Within ~5 s:
   `Select-String BackMaster C:\Windows\System32\drivers\etc\hosts`, and `chatgpt.com` fails to load in
   Edge/Chrome (close old tabs first; open connections can survive). The tray icon turns orange.
4. As the student: press the hotkey, enter a wrong phrase → "Грешна фраза"; 5 more → lockout message.
   The right phrase opens settings; Save asks for an administrator password.
5. As the student, try to edit `C:\ProgramData\BackMaster\policy.json` or read `private\` → access denied.
6. Set mode back to **По график**. Uninstall: `msiexec /x artifacts\BackMaster.msi` → the hosts section is removed.

Emergency removal, even if the MSI is gone (elevated; add `-DryRun` to preview):
`powershell -ExecutionPolicy Bypass -File .\scripts\Remove-BackMaster.ps1`

## Not done yet

- Companion (fox) artwork and the per-site "blocked" pop-up: hosts blocking shows the browser's own error page.
- App blocking (`app_blocked` message) and temporary exceptions (`exception_*` messages).
- The health checker in `Core/Maintenance` has no Windows `ISystemProbe`, manifest generator or scheduled task yet.
- Hosts blocking covers listed names only (no wildcards) and does not stop VPNs/proxies or browsers' own DoH where policy is not honoured.
