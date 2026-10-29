using System.Globalization;
using PowerPlan.Core;

namespace PowerPlan.Platform.Mac;

public sealed class MacPowerController : IPowerController
{
    readonly int shutdownGraceSeconds;
    readonly ILog log;
    readonly ICommandRunner runner;

    public MacPowerController(int shutdownGraceSeconds, ILog log) : this(shutdownGraceSeconds, log, ProcessCommandRunner.Instance)
    {
    }

    internal MacPowerController(int shutdownGraceSeconds, ILog log, ICommandRunner runner)
    {
        this.shutdownGraceSeconds = shutdownGraceSeconds;
        this.log = log;
        this.runner = runner;
    }

    // macOS has no shutdown countdown like shutdown.exe /t, so a dialog gives the user the chance to cancel.
    public void Shutdown() => ShutDownOrRestart("shut down", "Shut down now", "shut down");

    public void Restart() => ShutDownOrRestart("restart", "Restart now", "restart");

    // Macs have no Modern Standby problem: a pmset wake fires from ordinary sleep.
    public void Sleep() => SleepNow();

    // pmset decides between sleep and hibernate itself (hibernatemode, standby); there is no "hibernate now".
    public void Hibernate() => SleepNow();

    public void TurnDisplayOn()
    {
        // -u declares user activity, which turns the display on after a scheduled wake; -t ends the assertion.
        var result = runner.Run(Tools.Caffeinate, "-u", "-t", "5");
        log.Write(result.Succeeded ? "Display on requested" : $"caffeinate failed ({result.ExitCode}): {result.Text}");
    }

    void SleepNow()
    {
        log.Write("Sleeping");
        var result = runner.Run(Tools.Pmset, "sleepnow");
        if (!result.Succeeded) log.Write($"pmset sleepnow failed ({result.ExitCode}): {result.Text}");
    }

    void ShutDownOrRestart(string verb, string confirmButton, string systemEventsCommand)
    {
        var grace = Math.Max(0, shutdownGraceSeconds);
        if (grace > 0 && !ConfirmedOrTimedOut(verb, confirmButton, grace)) return;

        log.Write($"System Events {systemEventsCommand}");
        // Needs the Automation permission for System Events; an app with unsaved changes can still stop it.
        var result = AppleScript.Run(runner, $"tell application \"System Events\" to {systemEventsCommand}");
        if (!result.Succeeded)
            throw new InvalidOperationException($"Could not {verb} the Mac ({result.ExitCode}): {result.Text}");
    }

    /// <summary>False only when the user pressed Cancel; the dialog gives up (and the action goes ahead) after the grace period.</summary>
    bool ConfirmedOrTimedOut(string verb, string confirmButton, int grace)
    {
        var seconds = grace.ToString(CultureInfo.InvariantCulture);
        var message = $"NapPilot will {verb} your Mac in {seconds} seconds.";
        log.Write($"Asking before {verb} ({seconds}s)");
        var result = AppleScript.Run(runner,
            $"display dialog {AppleScript.Quote(message)} with title \"NapPilot\" " +
            $"buttons {{\"Cancel\", {AppleScript.Quote(confirmButton)}}} default button 2 cancel button 1 " +
            $"with icon caution giving up after {seconds}");

        if (AppleScript.WasCanceled(result))
        {
            log.Write($"The user cancelled the scheduled {verb}");
            return false;
        }
        // The user scheduled this; a dialog that cannot be shown must not silently stop it.
        if (!result.Succeeded) log.Write($"Warning dialog failed ({result.ExitCode}): {result.Text}; going ahead");
        return true;
    }
}
