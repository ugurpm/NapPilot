using System.Runtime.InteropServices;
using System.Windows.Forms;
using PowerPlan.Core;
using static PowerPlan.Platform.Windows.NativeMethods;

namespace PowerPlan.Platform.Windows;

/// <summary>
/// Decides when a sleep should become hibernate. Pure so it can be tested without power events.
/// </summary>
internal sealed class SleepCatchGate(TimeSpan graceAfterAwake)
{
    DateTimeOffset lastAwake = DateTimeOffset.MinValue;
    bool converted;

    /// <summary>The screen came on or the user resumed the PC.</summary>
    public void OnAwake(DateTimeOffset now)
    {
        lastAwake = now;
        converted = false;
    }

    /// <summary>
    /// Called when the screen goes off or Windows announces suspend. The grace period skips the
    /// brief screen-off flicker seen right after a timer wake, which would otherwise re-hibernate the PC.
    /// </summary>
    public bool ShouldConvert(DateTimeOffset now, bool wakePending)
    {
        if (!wakePending || converted || now - lastAwake < graceAfterAwake) return false;
        converted = true;
        return true;
    }
}

/// <summary>
/// On Modern Standby hardware a timer cannot wake the PC from standby, only from hibernate.
/// While a wake is pending, this turns any sleep (Start menu, lid, screen timeout) into hibernate.
/// Sleeps PowerPlan started itself (<paramref name="sleepAlreadyRequested"/>) are already hibernate and left alone.
/// </summary>
public sealed class SleepCatcher(
    Func<bool> wakePending, Func<bool> sleepAlreadyRequested, Action hibernate, TimeProvider time, ILog log) : IDisposable
{
    readonly SleepCatchGate gate = new(TimeSpan.FromSeconds(30));
    Thread? thread;
    Listener? listener;

    public void Start()
    {
        var ready = new ManualResetEventSlim();
        thread = new Thread(() =>
        {
            listener = new Listener(this);
            ready.Set();
            Application.Run();
            listener.DestroyHandle();
        })
        { IsBackground = true, Name = "PowerPlan sleep catcher" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        log.Write("Sleep catcher listening");
    }

    public void Dispose()
    {
        if (listener is not null) PostMessage(listener.Handle, WM_CLOSE, 0, 0);
        thread?.Join(TimeSpan.FromSeconds(2));
    }

    void OnAwake(string reason)
    {
        gate.OnAwake(time.GetUtcNow());
        log.Write($"Sleep catcher: awake ({reason})");
    }

    void OnSleeping(string reason)
    {
        if (sleepAlreadyRequested())
        {
            log.Write($"Sleep catcher: {reason}, PowerPlan already started this sleep");
            return;
        }
        if (!gate.ShouldConvert(time.GetUtcNow(), wakePending())) return;
        log.Write($"Sleep catcher: {reason} while a wake is pending, switching to hibernate");
        // Calling SetSuspendState from the window thread would block power broadcasts.
        new Thread(() => hibernate()) { IsBackground = true }.Start();
    }

    sealed class Listener : NativeWindow
    {
        readonly SleepCatcher owner;

        public Listener(SleepCatcher owner)
        {
            this.owner = owner;
            CreateHandle(new CreateParams());
            var display = GUID_CONSOLE_DISPLAY_STATE;
            RegisterPowerSettingNotification(Handle, ref display, 0);
            // Suspend/resume broadcasts reach top-level windows without registering;
            // RegisterSuspendResumeNotification would deliver each one a second time.
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_CLOSE)
            {
                Application.ExitThread();
                return;
            }
            if (m.Msg == WM_POWERBROADCAST)
            {
                switch ((int)m.WParam)
                {
                    case PBT_POWERSETTINGCHANGE:
                        var setting = Marshal.PtrToStructure<POWERBROADCAST_SETTING>(m.LParam);
                        if (setting.Data == 0) owner.OnSleeping("display off");
                        else if (setting.Data == 1) owner.OnAwake("display on");
                        break;
                    case PBT_APMSUSPEND:
                        owner.OnSleeping("suspend");
                        break;
                    case PBT_APMRESUMESUSPEND:
                        owner.OnAwake("user resume");
                        break;
                }
                m.Result = 1;
            }
            base.WndProc(ref m);
        }
    }
}
