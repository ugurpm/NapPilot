# Changelog

All notable changes to NapPilot are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.3] - 2026-11-06

### Added

- Parental mode: set a password under Settings to require it before any schedule can be added, edited, paused, resumed
  or deleted, or history cleared. The schedule list, history, filters and search stay visible to everyone. The password
  is hashed, never stored in plain text; once entered it unlocks the window until it's closed or locked again.

## [1.0.2] - 2026-11-02

No changes to the app: it behaves exactly like 1.0.1.

### Changed

- Documentation: this changelog, a release checklist, and notes on how installed copies update and start at sign-in.

## [1.0.1] - 2026-11-02

### Added

- NapPilot starts in the tray when you sign in. Installed copies turn this on once, on their first start; after that your
  choice is kept, whether you switch it off in NapPilot, in Windows *Settings → Apps → Startup* or in macOS *Login Items*.
- When an update has been downloaded, the tray menu offers **Restart to update** and the tray tooltip says it is ready.

### Fixed

- On Windows, the *Start at sign-in* setting now shows the real state when it was switched off in Windows Startup apps,
  and turning it on in NapPilot really turns it back on.

## [1.0.0] - 2026-10-29

First public release.

### Added

- Schedule **shut down**, **restart**, **sleep** and **wake** (with the screen turned on): once on a date, every day,
  weekdays, weekends or any days you pick.
- One list of upcoming, paused and past schedules, with a colour per action, filters (all / upcoming / past, by action,
  by name) and edit, pause / resume and delete. Past runs can be deleted one by one or all at once.
- Run history: done, skipped because the computer was off or asleep, failed, and whether a wake really woke the computer.
- A missed shutdown, restart or sleep never fires later (runs that start more than 5 minutes late are skipped).
- A cancellable warning before every shutdown or restart (60 seconds by default).
- Wake on Windows laptops with Modern Standby: while a wake is pending, sleep becomes hibernate so the timer can fire.
- macOS support (launchd and `pmset`); not yet tested on a real Mac.
- Tray / menu-bar app with a single instance, **Sleep now**, and a Settings tab (dry-run, warning time, start at sign-in,
  updates, **Report a bug**).
- Installers and portable zips for Windows (x64, ARM64) and macOS (Apple silicon, Intel), automatic updates from GitHub
  Releases, SHA-256 checksums and SLSA build provenance.

[Unreleased]: https://github.com/ugurpm/NapPilot/compare/v1.0.3...HEAD
[1.0.3]: https://github.com/ugurpm/NapPilot/compare/v1.0.2...v1.0.3
[1.0.2]: https://github.com/ugurpm/NapPilot/compare/v1.0.1...v1.0.2
[1.0.1]: https://github.com/ugurpm/NapPilot/compare/v1.0.0...v1.0.1
[1.0.0]: https://github.com/ugurpm/NapPilot/releases/tag/v1.0.0
