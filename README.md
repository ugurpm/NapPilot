# PowerPlan

Schedule shutdown, restart, sleep and wake on Windows (macOS planned), built with .NET 10 and Avalonia.

## Layout

| Project | What it holds |
|---|---|
| `src/PowerPlan.Core` | Schedules, next-run calculation (DST-aware), validation, wake advice, the services that run schedules. No UI or OS code. |
| `src/PowerPlan.Platform.Windows` | Task Scheduler backend, power actions, capability probe, wake verification, and the sleep catcher. |
| `src/PowerPlan.App` | Avalonia window and tray icon. `PowerPlan.App.exe --run <id>` runs one schedule headless (used by Task Scheduler). |
| `tests/*` | xUnit tests for Core and the Windows layer. |

## Run

```bash
dotnet test
```

```bash
dotnet run --project src/PowerPlan.App
```

Data, settings and the log live in `%APPDATA%\PowerPlan`. Pass `--tray` to start without opening the window.

## Dry-run

`settings.json` starts with `"DryRun": true`, so power actions are only logged. Set it to `false` to make them real.
Scheduled shutdown and restart give `ShutdownGraceSeconds` (default 60) of warning; `shutdown /a` cancels.

## How wake works on Modern Standby PCs

Tested on the development laptop (Dell XPS 15 9520, Modern Standby only):

- A Task Scheduler timer can wake the PC from **hibernate**, not from Modern Standby.
- So while a wake is scheduled, the tray app's sleep catcher turns any sleep (Start menu, lid, screen timeout) into hibernate.
- On wake, PowerPlan posts `SC_MONITORPOWER` to turn the screen on, then checks the event log to confirm its timer caused the wake.
- Wake timers are off on battery in the default Windows power plan, so wake needs AC power.
- PowerPlan must be running in the tray for the sleep catcher to work.
