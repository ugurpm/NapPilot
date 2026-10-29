# Roadmap

Status as of 2026-10-29.

## Done

- Core scheduling: once / every day / weekdays / weekends / custom days, DST-aware next run, validation.
- Run history (done, dry-run, skipped because late, failed, wake check result), capped at 500 records.
- Windows: Task Scheduler backend, shutdown / restart (cancellable grace period), sleep-as-hibernate, screen on after wake,
  wake verification from the event log, capability probe, sleep catcher, start at sign-in (Run key).
- macOS layer (written and unit-tested, **not yet run on a Mac**): LaunchAgents, `pmset` wakes, power actions, login item.
- App renamed to **NapPilot** with its own icon; data folder migrates from `PowerPlan`.
- Window: action picker with colours, time picker with quick picks, custom days, live "next run" preview; one list of
  upcoming, paused and past schedules with filters (all / upcoming / past, action, search), edit, pause/resume, delete,
  clear past. Settings tab: dry-run, grace period, start at sign-in, updates.
- Single instance per user; a second launch shows the running window.
- Dry-run on in Debug builds, off in Release builds.
- Velopack installers and auto-update from GitHub Releases; release workflow for win-x64, win-arm64, osx-arm64, osx-x64.
- 121 unit tests (Core, Windows, Mac).
- Real-hardware wake verified end to end on Windows (see [WAKE-FINDINGS.md](WAKE-FINDINGS.md)).

## Next

1. **First release**: make the repo public, push `v1.0.0`, check the workflow output (especially the Mac asset names), install
   the Windows build and let it update to a `v1.0.1`.
2. **Test on a real Mac**: launchd job start after a `pmset` wake, the admin prompt, shutdown dialog, menu-bar icon,
   `.pkg` install, Gatekeeper flow. Expect fixes.
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
- Installers are ~65 MB (self-contained .NET + Avalonia); trimming is not enabled.
