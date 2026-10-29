# PowerPlan: notes for Claude Code

Cross-platform (Windows now, macOS later) desktop app that schedules shutdown, restart, sleep and wake.
.NET 10, Avalonia 12, CommunityToolkit.Mvvm, xUnit 2.

Read these before changing behaviour:

- [docs/WAKE-FINDINGS.md](docs/WAKE-FINDINGS.md): what was measured on real hardware and why the wake design looks the way it does.
- [docs/ROADMAP.md](docs/ROADMAP.md): what is done, what is next, known issues.
- [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md): setup, running, manual testing.

## Commands

```bash
dotnet build
dotnet test
dotnet run --project src/PowerPlan.App
```

On macOS only `tests/PowerPlan.Core.Tests` runs; the Windows projects target `net10.0-windows`.

## Architecture

- `src/PowerPlan.Core`: no OS or UI code. Schedules, `ScheduleCalculator` (DST-aware next run), `ScheduleValidator`,
  `WakeAdvisor`, `SleepPolicy`, `ScheduleService`, `ScheduledRunner`, `SleepRequestMarker`, interfaces in `Abstractions.cs`.
- `src/PowerPlan.Platform.Windows`: `TaskSchedulerBackend` (builds task XML, registers with `schtasks.exe`),
  `WindowsPowerController`, `WindowsCapabilityProbe`, `WindowsWakeVerifier`, `SleepCatcher`.
- `src/PowerPlan.App`: Avalonia window + tray. `AppServices` is the composition root (`#if WINDOWS` picks the platform layer).
  `PowerPlan.App.exe --run <id>` runs one schedule headless; Task Scheduler calls it.
- New OS-specific code goes behind a Core interface, with a fake in the tests.

## Rules for this project

- Dry-run (`settings.json` `"DryRun": true`) is the default. Keep it that way; never make tests or dev runs perform real power actions.
- Ask the user before installing software, changing Windows settings (power plan, wake timers, BIOS) or putting the PC to sleep.
- Each Task Scheduler task is a one-off trigger; the app re-arms the next occurrence after each run and at startup.
- `StartWhenAvailable` stays false (a missed shutdown must never fire later). Only Wake tasks get `WakeToRun`.
- Match the existing style: file-scoped namespaces, primary constructors, short comments that explain *why*.

## Gotchas

- Claude Code's own shell may run in a sandbox with a virtualized `%APPDATA%`. The real PowerPlan data (what Task Scheduler
  and the user's app use) is only visible from the user's terminal panel. Use `scripts/Show-PowerPlanState.ps1` there.
- On Modern Standby PCs `SetSuspendState(false, …)` hibernates; timers only wake the PC from hibernate; the screen needs
  `SC_MONITORPOWER` after a timer wake. Details in docs/WAKE-FINDINGS.md.
- Event log message text is localized; match on task names (`PowerPlan-<guid N>`), not on English phrases.
