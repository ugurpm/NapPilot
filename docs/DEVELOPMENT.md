# Development guide

## Set up a new machine

1. Install the **.NET 10 SDK** (10.0.403 or later): https://dotnet.microsoft.com/download/dotnet/10.0
2. Install **Git**. On Windows, Git for Windows includes Git Credential Manager.
3. Install an editor: **VS Code** with the **C# Dev Kit** extension (used so far), or Rider / Visual Studio 2026.
4. Clone and set your commit identity for this repo:

   ```bash
   git clone https://ugurpm@github.com/ugurpm/PowerPlan.git
   ```

   ```bash
   git -C PowerPlan config user.email ugurpm@gmail.com
   ```

   The `ugurpm@` in the URL makes Git Credential Manager use the personal GitHub account even if a work account is
   also signed in. The first push opens a sign-in window.
5. Build and test:

   ```bash
   dotnet test
   ```

   NuGet packages restore automatically. No templates or workloads need installing.

Keep the clone outside OneDrive if you can (for example `C:\dev\PowerPlan`): `bin/` and `obj/` change on every build and
OneDrive syncs them.

## Run

```bash
dotnet run --project src/PowerPlan.App
```

- Pass `--tray` to start with only the tray icon.
- Closing the window keeps the app in the tray; use the tray menu's **Quit NapPilot** to exit.
- Only one copy runs per data directory; a second launch brings the first window forward.
- Data lives in `%APPDATA%\NapPilot` (macOS: `~/Library/Application Support/NapPilot`): `settings.json`, `schedules.json`,
  `history.json`, `nappilot.log`, `sleep-request.txt`. An old `PowerPlan` folder is moved there on first start.
- Set `NAPPILOT_DATA_DIR` to run a copy against another folder (demos, screenshots). Its schedules still register real
  Task Scheduler tasks, which run against the real data and do nothing; delete them afterwards.

## Dry-run mode

Debug builds create `settings.json` with `"DryRun": true`; Release builds (the installers) with `false`. The **Settings**
tab switches it without a restart. In dry-run every power action (shutdown, restart, sleep, hibernate,
screen on) is only written to `powerplan.log` as `[dry-run] would …`. Everything else is real: schedules are saved,
Task Scheduler tasks are registered and fire on time, the sleep catcher listens. This lets you test scheduling all day
without the PC turning off.

Turn it off only for a deliberate real test, and restart the app afterwards (settings are read at startup):

```bash
.\scripts\Set-NapPilotDryRun.ps1 -Enabled $false
```

```bash
.\scripts\Set-NapPilotDryRun.ps1 -Enabled $true
```

With dry-run off, scheduled shutdown and restart wait `ShutdownGraceSeconds` (default 60) and can be cancelled with
`shutdown /a`.

## Manual testing

```bash
.\scripts\Show-NapPilotState.ps1
```

Shows settings, every `NapPilot-*` task (next run, last result, WakeToRun) and the end of the log.

A quick check in dry-run: add a **Once** schedule (today, two minutes ahead), wait, then run the script. The log should
show `Running '<name>'` and `[dry-run] would …` at that minute.

A real wake test (needs AC power, dry-run off):

1. Add a **Wake**, **Once**, today, about 4 minutes ahead.
2. Click **Start → Power → Sleep** and leave the PC alone.
3. The PC should hibernate (the log shows `Sleep catcher: display off while a wake is pending, switching to hibernate`),
   wake at the set time with the screen on, and log `Wake check for '<name>': WokeOnSchedule`.
4. Turn dry-run back on.

## Tests

- `tests/PowerPlan.Core.Tests`: scheduling, DST, validation, wake advice, service and runner (with fakes), JSON store,
  sleep-request marker. Runs on any OS.
- `tests/PowerPlan.Platform.Windows.Tests`: task XML, the sleep catcher's decision logic, the Run-key login item (on a
  throwaway registry key). Windows only.
- `tests/PowerPlan.Platform.Mac.Tests`: launchd plists, pmset wake bookkeeping, power commands, all with a fake command
  runner. Runs on any OS.

Nothing in the test suites touches real power state or Task Scheduler.

## macOS

`PowerPlan.App` targets `net10.0` on macOS (and when publishing for `osx-*`) and uses `src/PowerPlan.Platform.Mac`:
a per-user LaunchAgent per run (`~/Library/LaunchAgents/com.nappilot.run.*`), `pmset schedule wake` for wakes (one admin
password prompt that schedules the next 30 days), `osascript` for shutdown/restart with a cancellable dialog, and
`pmset sleepnow` for sleep. None of it has run on a real Mac yet; test it there before relying on it.

## Icons

`src/PowerPlan.App/Assets/nappilot.svg` is the source. After changing it, regenerate the `.png`, `.ico`, `.icns` and the
README logo (uses headless Edge, nothing to install):

```bash
.\scripts\Build-Icons.ps1
```

## Releasing

Releases are built by [.github/workflows/release.yml](../.github/workflows/release.yml) when a `v*` tag is pushed. It tests,
packs win-x64 and win-arm64 (on Windows) and osx-arm64 and osx-x64 (on macOS) with Velopack, and creates the GitHub release
with the installers, portable zips and the update feed (`releases.<channel>.json`). Installed copies check that feed every
6 hours and offer **Restart to update**.

```bash
git tag v1.0.1
```

```bash
git push origin v1.0.1
```

The repo must be public: the app and the README download links read releases anonymously.

To build the Windows packages locally (output in `releases/`), install `vpk` once and run the script the workflow uses:

```bash
dotnet tool install -g vpk --version 1.2.161
```

```bash
.\scripts\Pack-Release.ps1 -Runtime win-x64 -Version 1.0.0
```

Builds are not code-signed: Windows SmartScreen and macOS Gatekeeper warn on first launch (the README explains how to
continue). Signing needs a code-signing certificate and an Apple Developer account; `vpk pack` can sign once they exist.
