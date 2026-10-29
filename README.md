# PowerPlan

Schedule shutdown, restart, sleep and wake on Windows (macOS planned), built with .NET 10 and Avalonia.

## Quick start

Needs the .NET 10 SDK. Full setup for a new machine: [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).

```bash
dotnet test
```

```bash
dotnet run --project src/PowerPlan.App
```

Data, settings and the log live in `%APPDATA%\PowerPlan`. Pass `--tray` to start without opening the window.

**Dry-run is on by default**: power actions are only logged, so nothing turns the PC off while you develop. Schedules,
Task Scheduler tasks and the sleep catcher are still real. See [Dry-run mode](docs/DEVELOPMENT.md#dry-run-mode).

## Layout

| Project | What it holds |
|---|---|
| `src/PowerPlan.Core` | Schedules, next-run calculation (DST-aware), validation, wake advice, the services that run schedules. No UI or OS code. |
| `src/PowerPlan.Platform.Windows` | Task Scheduler backend, power actions, capability probe, wake verification, and the sleep catcher. |
| `src/PowerPlan.App` | Avalonia window and tray icon. `PowerPlan.App.exe --run <id>` runs one schedule headless (used by Task Scheduler). |
| `tests/*` | xUnit tests for Core and the Windows layer. |
| `scripts/` | Manual-testing helpers: show state, switch dry-run. |

## How wake works on Modern Standby PCs

- A Task Scheduler timer can wake the PC from **hibernate**, not from Modern Standby.
- So while a wake is scheduled, the tray app turns any sleep (Start menu, lid, screen timeout) into hibernate.
- On wake, PowerPlan turns the screen on and checks the event log to confirm its timer caused the wake.
- Wake needs AC power on the test laptop, and PowerPlan running in the tray.

Measurements and reasoning: [docs/WAKE-FINDINGS.md](docs/WAKE-FINDINGS.md).

## Docs

- [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md): new machine setup, running, dry-run, manual tests.
- [docs/WAKE-FINDINGS.md](docs/WAKE-FINDINGS.md): hardware test results behind the wake design.
- [docs/ROADMAP.md](docs/ROADMAP.md): what is done, what is next, known issues.
- [CLAUDE.md](CLAUDE.md): project notes for Claude Code sessions.
