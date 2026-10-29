using System.Diagnostics;
using PowerPlan.Core;
using static PowerPlan.Platform.Windows.NativeMethods;

namespace PowerPlan.Platform.Windows;

public sealed class WindowsPowerController(PowerCapabilities capabilities, int shutdownGraceSeconds, ILog log) : IPowerController
{
    // shutdown.exe shows a warning for the grace period, and `shutdown /a` cancels it.
    public void Shutdown() => RunShutdownExe("/s");

    public void Restart() => RunShutdownExe("/r");

    public void Sleep()
    {
        // Timers cannot wake Modern Standby, so sleep must be hibernate when there is no S3.
        if (!capabilities.S3Sleep) Hibernate();
        else Suspend(hibernate: false);
    }

    public void Hibernate() => Suspend(hibernate: true);

    public void TurnDisplayOn()
    {
        // Tested on the Modern Standby dev laptop: this ends standby after a timer wake; ES_DISPLAY_REQUIRED alone does not.
        PostMessage(HWND_BROADCAST, WM_SYSCOMMAND, SC_MONITORPOWER, -1);
        log.Write("Display on requested");
    }

    void Suspend(bool hibernate)
    {
        log.Write(hibernate ? "Hibernating" : "Sleeping");
        if (!SetSuspendState(hibernate, false, false))
            log.Write($"SetSuspendState failed: error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
    }

    void RunShutdownExe(string mode)
    {
        var grace = Math.Max(0, shutdownGraceSeconds);
        log.Write($"shutdown.exe {mode} in {grace}s");
        var start = new ProcessStartInfo("shutdown.exe") { CreateNoWindow = true, UseShellExecute = false };
        foreach (var arg in new[] { mode, "/t", grace.ToString(), "/c", "NapPilot scheduled this. Run 'shutdown /a' to cancel." })
            start.ArgumentList.Add(arg);
        Process.Start(start)?.WaitForExit();
    }
}
