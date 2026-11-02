using Microsoft.Win32;
using PowerPlan.Core;

namespace PowerPlan.Platform.Windows;

/// <summary>
/// Per-user Run key entry: no admin rights needed. Settings → Apps → Startup and Task Manager's Startup tab don't delete
/// it when the user switches it off; they mark it disabled under StartupApproved, so both keys decide whether it is on.
/// </summary>
public sealed class WindowsLoginItem(
    string exePath, string runKeyPath = WindowsLoginItem.RunKey, string approvedKeyPath = WindowsLoginItem.ApprovedKey) : ILoginItem
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    const string ValueName = "NapPilot";

    string Command => $"\"{exePath}\" --tray";

    public bool IsEnabled
    {
        get
        {
            using var run = Registry.CurrentUser.OpenSubKey(runKeyPath);
            if (run?.GetValue(ValueName) is not string value || value != Command) return false;

            // The first byte is 02 (or 06) when enabled and 03 (or 07) when the user switched it off.
            using var approved = Registry.CurrentUser.OpenSubKey(approvedKeyPath);
            return approved?.GetValue(ValueName) is not byte[] { Length: > 0 } state || (state[0] & 1) == 0;
        }
    }

    public void SetEnabled(bool enabled)
    {
        using (var run = Registry.CurrentUser.CreateSubKey(runKeyPath))
        {
            if (enabled) run.SetValue(ValueName, Command);
            else run.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        // Clear any "disabled" mark, so turning it on in NapPilot really turns it on.
        using var approved = Registry.CurrentUser.OpenSubKey(approvedKeyPath, writable: true);
        approved?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
