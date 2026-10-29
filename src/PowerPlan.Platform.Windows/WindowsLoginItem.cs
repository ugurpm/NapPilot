using Microsoft.Win32;
using PowerPlan.Core;

namespace PowerPlan.Platform.Windows;

/// <summary>Per-user Run key entry: no admin rights needed, and Windows' Startup apps page can turn it off.</summary>
public sealed class WindowsLoginItem(string exePath, string runKeyPath = WindowsLoginItem.RunKey) : ILoginItem
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "NapPilot";

    string Command => $"\"{exePath}\" --tray";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(runKeyPath);
            return key?.GetValue(ValueName) is string value && value == Command;
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(runKeyPath);
        if (enabled) key.SetValue(ValueName, Command);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
