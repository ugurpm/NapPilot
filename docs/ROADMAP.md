# Roadmap

Status as of 2026-10-29.

## Done

- Core scheduling: once / every day / weekdays / weekends, DST-aware next run, validation.
- Windows: Task Scheduler backend, shutdown / restart (cancellable grace period), sleep-as-hibernate, screen on after wake,
  wake verification from the event log, capability probe, sleep catcher.
- Avalonia window (add / delete schedules, wake notes, dry-run banner) and tray icon (Open, Sleep now, Quit).
- Dry-run mode on by default.
- Real-hardware wake verified end to end (see [WAKE-FINDINGS.md](WAKE-FINDINGS.md)).
- 48 unit tests.

## Next

1. **Verify the double-hibernate fix with a real sleep**: dry-run off, schedule a wake, tray **Sleep now**; the log
   should show `PowerPlan already started this sleep`.
2. **Countdown before shutdown/restart**: an in-app window with Cancel instead of relying on `shutdown.exe`'s message.
3. **Start with Windows**: register the tray app to start at sign-in (per-user Run key or a logon task) so the sleep
   catcher is always running. Ask before adding it.
4. **Single instance**: a second launch should bring the first window forward instead of starting another tray icon.
5. **Edit and enable/disable schedules** in the UI, and custom day sets (Mon/Wed/Fri).
6. **Settings UI** for dry-run and the grace period.
7. **Repeat the wake tests**: several more Start-menu catches, lid close, overnight; and on a second Windows PC (ideally one
   with S3 sleep).
8. **macOS layer**: `pmset schedule` / `pmset repeat` (needs root, so a privileged helper or an admin prompt), launchd for
   running actions, capability probe. Check Apple Silicon power-on limits.
9. **Packaging**: self-contained publish, installer (MSIX or Inno Setup), signed builds.

## Known issues

- On startup the app tries to delete tasks for one-off schedules that already ran; if the task is already gone the log
  gets a harmless `schtasks /Delete … returned 1` line.
- Scheduled wake needs AC power on the dev laptop (wake timers disabled on battery) and needs the tray app running.
- Tasks point at the exe path at registration time; after moving the build output, start the app once so it re-arms.
