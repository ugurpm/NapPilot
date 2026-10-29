# Wake findings

Measured on 2026-10-29 on the development laptop. These results drive the wake design; re-test on any new hardware.

## Test machine

| Item | Value |
|---|---|
| Hardware | Dell XPS 15 9520, BIOS 1.42.0, battery |
| OS | Windows 11 Home 10.0.26300, user is not an administrator |
| Sleep states (`powercfg /a`) | Standby (S0 Low Power Idle / Modern Standby), Hibernate, Fast Startup. **No S1/S2/S3** (firmware does not support them) |
| "Allow wake timers" | AC: Enable. Battery: Disable |
| Overnight behaviour | Modern Standby moves to hibernate by itself after a while |

## Results

| Test | Result |
|---|---|
| `SetSuspendState(hibernate: false)` | **Hibernates** (no S3 to fall back to), does not enter Modern Standby |
| Task Scheduler task with WakeToRun, PC in **hibernate**, on AC | **Wakes on time** (5/5, including the run through the app). `powercfg /lastwake` names the task |
| Same, PC in **Modern Standby** via Start → Sleep | **Does not wake.** The task only ran when the user moved the mouse |
| After a timer wake, screen state | Screen stays **off**; the PC sits in Modern Standby and re-sleeps after ~2 min without input |
| `SetThreadExecutionState(ES_DISPLAY_REQUIRED)` after wake | Does **not** turn the screen on |
| `PostMessage(HWND_BROADCAST, WM_SYSCOMMAND, SC_MONITORPOWER, -1)` after wake | **Turns the screen on** (lock screen), event 507 reason `SC_MONITORPOWER` |
| Synthetic mouse move after wake | Not needed once `SC_MONITORPOWER` is sent |
| Catching Start → Sleep: hidden window gets display-off (`GUID_CONSOLE_DISPLAY_STATE` = 0), then `PBT_APMSUSPEND` ~150 ms later; calling `SetSuspendState(hibernate: true)` from a worker thread | **Hibernates**, and the timer then wakes it with the screen on |

The first end-to-end run through the app (dry-run off) logged the full chain: `Sleep catcher: display off while a wake
is pending, switching to hibernate` → task ran on time → `Display on requested` → `Wake check: WokeOnSchedule`.

## Resulting design

1. **Sleep is hibernate while a wake is pending.** `WindowsPowerController.Sleep` hibernates when there is no S3.
   The tray app's `SleepCatcher` turns any other sleep (Start menu, lid, screen timeout) into hibernate.
2. **Wake = Task Scheduler task with WakeToRun** running `NapPilot.exe --run <id> --at <unix seconds>`. No admin rights needed.
3. **On wake, post `SC_MONITORPOWER`** so the screen comes on.
4. **Verify every wake** by reading the latest Power-Troubleshooter event 1 and checking it names the task.
5. **Tell the user the limits** (`WakeAdvisor`): wake needs AC power here, and NapPilot must be running in the tray.

## Details worth knowing

- A screen-off flicker happens right after a timer wake (on, off 0.1 s later, on). `SleepCatchGate` ignores screen-off
  for 30 s after the PC is awake so it does not re-hibernate.
- When NapPilot starts a sleep itself, the screen then goes off and the catcher would ask to hibernate a second time.
  `SleepRequestMarker` (a timestamp file shared with the `--run` process) makes the catcher stand down for 30 s.
- Windows wakes the PC ~10–15 s before the trigger time to prepare; the task still runs at the trigger time.
- Top-level windows receive suspend/resume broadcasts without `RegisterSuspendResumeNotification`; registering as well
  delivers each event twice.

## Not possible from software on this laptop

- **Power on from fully off** (shutdown, including Fast Startup): needs the BIOS "Auto On Time" setting or Wake-on-LAN
  from another device.
- **Wake from Modern Standby** without converting to hibernate (a third-party driver, ModernWakeup, claims to do it; not
  evaluated).
- **Wake on battery** while "Allow wake timers" is disabled on DC. Changing it is a Windows power-plan change and needs
  the user's consent.
