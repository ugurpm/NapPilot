# Roadmap

Status as of 2026-11-02. Released changes are listed in [CHANGELOG.md](../CHANGELOG.md).

## Done

- Core scheduling: once / every day / weekdays / weekends / custom days, DST-aware next run, validation.
- Run history (done, dry-run, skipped because late, failed, wake check result), capped at 500 records.
- Windows: Task Scheduler backend, shutdown / restart (cancellable grace period), sleep-as-hibernate, screen on after wake,
  wake verification from the event log, capability probe, sleep catcher, start at sign-in (Run key, honouring the
  Windows Startup apps switch).
- macOS layer (written and unit-tested, **not yet run on a Mac**): LaunchAgents, `pmset` wakes, power actions, login item.
- App renamed to **NapPilot** with its own icon; data folder migrates from `PowerPlan`.
- Window: action picker with colours, time picker with quick picks, custom days, live "next run" preview; one list of
  upcoming, paused and past schedules with filters (all / upcoming / past, action, search), edit, pause/resume, delete,
  clear past. Settings tab: dry-run, grace period, start at sign-in, updates, report a bug.
- Single instance per user; a second launch shows the running window.
- Installed copies start at sign-in by default (applied once per install; switching it off in the app or in Windows
  Startup apps / macOS Login Items is respected).
- Dry-run on in Debug builds, off in Release builds.
- Velopack installers and auto-update from GitHub Releases (full and delta packages); release workflow for win-x64,
  win-arm64, osx-arm64, osx-x64 with SLSA provenance and checksums. Update-ready is offered in the window and the tray.
- Releases: v1.0.0 (2026-10-29), v1.0.1 and v1.0.2 (2026-11-02; 1.0.2 is docs only). Auto-update verified on a real install: a v1.0.0 copy found and
  offered v1.0.1.
- Public repo hardened: protected `main` and `v*` tags, CodeQL, dependency review, Dependabot, OpenSSF Scorecard,
  secret scanning with push protection, private vulnerability reporting.
- 126 unit tests (Core, Windows, Mac).
- Real-hardware wake verified end to end on Windows (see [WAKE-FINDINGS.md](WAKE-FINDINGS.md)).

## Next

1. **Test on a real Mac**: `.pkg` install and Gatekeeper flow, menu-bar icon, login item, launchd job start after a
   `pmset` wake, the admin prompt, shutdown dialog. Expect fixes.
2. **Turn on release immutability** in the repo settings, now that two releases went through the workflow cleanly.
3. **Verify the double-hibernate fix with a real sleep**: schedule a wake, tray **Sleep now**; the log should show
   `NapPilot already started this sleep`.
4. **Countdown before shutdown/restart on Windows**: an in-app window with Cancel instead of `shutdown.exe`'s message.
5. **Code signing** (Windows certificate, Apple Developer ID + notarization) to remove the first-launch warnings.
6. **Repeat the wake tests**: several more Start-menu catches, lid close, overnight; and on a second Windows PC (ideally one
   with S3 sleep).
7. **Localisation** (Turkish first): the UI strings are English only.

## Known issues

- On startup the app tries to delete tasks for one-off schedules that already ran; if the task is already gone the log
  gets a harmless `schtasks /Delete … returned 1` line.
- Scheduled wake needs AC power on the dev laptop (wake timers disabled on battery) and needs the tray app running.
- Tasks point at the exe path at registration time; after moving the build output, start the app once so it re-arms.
  Installed copies live in `%LocalAppData%\NapPilot\current`, which stays the same across updates.
- Copies of v1.0.0 don't start at sign-in, so they only find an update when someone opens NapPilot.
- Installers are ~65 MB (self-contained .NET + Avalonia); trimming is not enabled. Updates after the first install are
  small delta packages (~0.2 MB for v1.0.0 → v1.0.1).
