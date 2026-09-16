# Kilo Session Manager

Portable Windows GUI for managing multiple long-running **Kilo Code CLI** project sessions.

The manager is deliberately **not** the owner of the Kilo process. Each running project gets a detached `KiloSessionHost.exe` process which owns a Windows ConPTY and the Kilo TUI. Closing, crashing, or Alt+F4'ing `KiloSessionManager.exe` only disconnects the GUI; it does not terminate Kilo.

## Intended workflow

1. Run `KiloSessionManager.exe`.
2. Click **Add project** and browse to a folder.
3. Give the project any display name. Leaving the name blank uses the folder name.
4. A new project starts with `kilo --auto`.
5. If a previously-started project no longer has a live host, selecting/starting it uses `kilo --auto --continue`.
6. Closing the manager leaves all live hosts/Kilo tasks running.
7. Reopening the manager reconnects to each live host when the project is selected and restores the recent terminal buffer.
8. **Terminate** is the explicit action that stops the Kilo process tree. A terminated project remains stopped until **Start / Resume** is clicked.

## Architecture

```text
KiloSessionManager.exe (WinForms + WebView2 + xterm.js)
          |
          | named pipe IPC
          v
KiloSessionHost.exe (detached, one per running project)
          |
          | Windows ConPTY
          v
cmd.exe /c kilo --auto [--continue]
```

Each `SessionHost` has its own named pipe and an 8 MiB terminal-output ring buffer. The ring buffer means output produced while the GUI is closed is visible again after reconnecting.

## Project states

- `●` Running: a validated `SessionHost` PID is alive.
- `○` Stopped/unexpected exit: selecting it may auto-resume with `--continue`.
- `■` Manually stopped: only **Start / Resume** starts it again.

Projects are stored in `data/projects.json` beside the executable, so the app remains portable. Display names are independent of folder names.

## Build

Requirements for building:

- Windows 10 1809+ (runtime requirement for ConPTY)
- .NET 8 SDK
- Node.js/npm (used only at build time to obtain local xterm.js assets)

```powershell
dotnet publish src/KiloSessionHost/KiloSessionHost.csproj -c Release -r win-x64 --self-contained true -o publish
dotnet publish src/KiloSessionManager/KiloSessionManager.csproj -c Release -r win-x64 --self-contained true -o publish
```

Run `publish/KiloSessionManager.exe`.

The build is self-contained for .NET. The GUI uses the Microsoft Edge WebView2 runtime, which is normally present on current Windows 10/11 installations. If WebView2 is missing, install the Evergreen WebView2 Runtime from Microsoft.

## CI / test coverage

The GitHub Actions Windows job compiles/publishes both executables, starts the real SessionHost against an interactive `cmd.exe` under ConPTY, sends terminal input over the named pipe, verifies ConPTY output, disconnects and reconnects, verifies the ring-buffer snapshot, sends `terminate`, and packages a portable Windows artifact.

The CI smoke test does not log into Kilo or run a real AI task because that would require a configured Kilo installation/account. The Kilo launch command is intentionally thin: `cmd.exe /c kilo --auto` or `cmd.exe /c kilo --auto --continue` in the selected project directory.

## Safety behavior

- Closing the Manager never calls terminate.
- Switching projects only disconnects/reconnects the named pipe; it does not touch the Kilo process.
- A running project cannot be removed from the registry; terminate it first. This avoids creating unmanaged/orphaned sessions.
- Runtime PID validation also checks the process start time to reduce the risk of treating a reused Windows PID as the original host.

## Current MVP limitations

- One managed live Kilo terminal per project folder.
- Resume uses Kilo's `--continue` behavior rather than pinning an exact Kilo session ID.
- The recent terminal history buffer is in SessionHost memory, so it survives Manager restarts but not a SessionHost crash or Windows reboot.
- There is no tray icon or auto-start yet.
