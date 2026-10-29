# NapPilot: notes for Claude Code

Cross-platform (Windows and macOS) desktop app that schedules shutdown, restart, sleep and wake. The product is called
**NapPilot**; the repo, projects and namespaces keep the old internal name `PowerPlan.*`.
.NET 10, Avalonia 12, CommunityToolkit.Mvvm, Velopack (installer + auto-update), xUnit 2.

Read these before changing behaviour:

- [docs/WAKE-FINDINGS.md](docs/WAKE-FINDINGS.md): what was measured on real hardware and why the wake design looks the way it does.
- [docs/ROADMAP.md](docs/ROADMAP.md): what is done, what is next, known issues.
- [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md): setup, running, manual testing, releasing.

## Commands

```bash
dotnet build
dotnet test
dotnet run --project src/PowerPlan.App
```

On macOS the Windows projects (`net10.0-windows`) don't build; `tests/PowerPlan.Core.Tests` and
`tests/PowerPlan.Platform.Mac.Tests` run anywhere. `dotnet publish src/PowerPlan.App -r osx-arm64` cross-compiles the Mac
app from Windows; packing it (`vpk`) needs a Mac, which the release workflow provides.

## Architecture

- `src/PowerPlan.Core`: no OS or UI code. Schedules, `RunRecord` history, `ScheduleCalculator` (DST-aware next run),
  `ScheduleValidator`, `ScheduleOverview` (upcoming / paused / past rows), `WakeAdvisor`, `SleepPolicy`, `ScheduleService`,
  `ScheduledRunner` (skips non-wake runs started more than 5 min late), `SleepRequestMarker`, interfaces in `Abstractions.cs`.
- `src/PowerPlan.Platform.Windows`: `TaskSchedulerBackend` (builds task XML, registers with `schtasks.exe`),
  `WindowsPowerController`, `WindowsCapabilityProbe`, `WindowsWakeVerifier`, `SleepCatcher`, `WindowsLoginItem` (Run key).
- `src/PowerPlan.Platform.Mac`: `MacScheduleBackend` (per-run LaunchAgents + `pmset schedule wake` via one admin prompt
  covering 30 days), `MacPowerController`, `MacCapabilityProbe`, `MacWakeVerifier`, `MacLoginItem`. Unverified on a real Mac.
- `src/PowerPlan.App` (assembly `NapPilot`): Avalonia window + tray. `AppServices` is the composition root
  (`#if WINDOWS` picks the platform layer). `NapPilot --run <id> --at <unix seconds>` runs one schedule headless; the OS
  scheduler calls it. `Updater` wraps Velopack's `UpdateManager` (GitHub Releases). `SingleInstance` keeps one tray app.
- New OS-specific code goes behind a Core interface, with a fake in the tests.

## Rules for this project

- Dry-run defaults to **on in Debug builds** and off in Release builds (what users install). Never make tests or dev runs
  perform real power actions; tests use fakes, and manual demos use `NAPPILOT_DATA_DIR` with their own settings.
- Ask the user before installing software, changing Windows settings (power plan, wake timers, BIOS) or putting the PC to sleep.
- Ask before pushing, tagging a release or changing the GitHub repo: a `v*` tag publishes to every installed copy.
- Each OS task is a one-off trigger; the app re-arms the next occurrence after each run and at startup.
- `StartWhenAvailable` stays false (a missed shutdown must never fire later). Only Wake tasks get `WakeToRun`.
- Match the existing style: file-scoped namespaces, primary constructors, short comments that explain *why*.

## Gotchas

- Claude Code's own shell may run in a sandbox with a virtualized `%APPDATA%`. The real NapPilot data (what Task Scheduler
  and the user's app use) is only visible from the user's terminal panel. Use `scripts/Show-NapPilotState.ps1` there.
- A running NapPilot locks `src/PowerPlan.App/bin/Debug`; stop it before `dotnet build`, or build `-c Release`.
- On Modern Standby PCs `SetSuspendState(false, …)` hibernates; timers only wake the PC from hibernate; the screen needs
  `SC_MONITORPOWER` after a timer wake. Details in docs/WAKE-FINDINGS.md.
- Event log message text is localized; match on task names (`NapPilot-<guid N>`), not on English phrases.
- Avalonia resource URIs use the assembly name: `avares://NapPilot/...`.
