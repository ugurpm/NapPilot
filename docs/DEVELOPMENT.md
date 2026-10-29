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
- Closing the window keeps the app in the tray; use the tray menu's **Quit** to exit.
- Data lives in `%APPDATA%\PowerPlan`: `settings.json`, `schedules.json`, `powerplan.log`, `sleep-request.txt`.

## Dry-run mode

`settings.json` is created with `"DryRun": true`. In dry-run every power action (shutdown, restart, sleep, hibernate,
screen on) is only written to `powerplan.log` as `[dry-run] would …`. Everything else is real: schedules are saved,
Task Scheduler tasks are registered and fire on time, the sleep catcher listens. This lets you test scheduling all day
without the PC turning off.

Turn it off only for a deliberate real test, and restart the app afterwards (settings are read at startup):

```bash
.\scripts\Set-PowerPlanDryRun.ps1 -Enabled $false
```

```bash
.\scripts\Set-PowerPlanDryRun.ps1 -Enabled $true
```

With dry-run off, scheduled shutdown and restart wait `ShutdownGraceSeconds` (default 60) and can be cancelled with
`shutdown /a`.

## Manual testing

```bash
.\scripts\Show-PowerPlanState.ps1
```

Shows settings, every `PowerPlan-*` task (next run, last result, WakeToRun) and the end of the log.

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
- `tests/PowerPlan.Platform.Windows.Tests`: task XML and the sleep catcher's decision logic. Windows only.

Nothing in the test suites touches real power state or Task Scheduler.

## macOS

`PowerPlan.App` targets `net10.0` on macOS and builds without the Windows layer; power actions fall back to dry-run and
scheduling only logs. Build and test the shared code there with:

```bash
dotnet test tests/PowerPlan.Core.Tests
```

The macOS layer (`pmset` / `launchd`) is not written yet; see [ROADMAP.md](ROADMAP.md).
