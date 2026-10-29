using System.Runtime.InteropServices;

namespace PowerPlan.Platform.Windows;

internal static class NativeMethods
{
    public const int WM_CLOSE = 0x0010;
    public const int WM_SYSCOMMAND = 0x0112;
    public const int WM_POWERBROADCAST = 0x0218;
    public const int SC_MONITORPOWER = 0xF170;
    public const int PBT_APMSUSPEND = 0x0004;
    public const int PBT_APMRESUMESUSPEND = 0x0007;
    public const int PBT_POWERSETTINGCHANGE = 0x8013;
    public static readonly IntPtr HWND_BROADCAST = 0xFFFF;

    public static readonly Guid GUID_CONSOLE_DISPLAY_STATE = new("6FE69556-704A-47A0-8F24-C28D936FDA47");
    public static readonly Guid GUID_SLEEP_SUBGROUP = new("238C9FA8-0AAD-41ED-83F4-97BE242C8F20");
    public static readonly Guid GUID_ALLOW_RTC_WAKE = new("BD3B718A-0680-4D9D-8AB2-E1D2B4AC806D");

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct POWERBROADCAST_SETTING
    {
        public Guid PowerSetting;
        public int DataLength;
        public int Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("powrprof.dll", SetLastError = true)]
    public static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

    /// <summary>Fills a SYSTEM_POWER_CAPABILITIES struct; read as raw bytes, see <see cref="WindowsCapabilityProbe"/>.</summary>
    [DllImport("powrprof.dll")]
    public static extern bool GetPwrCapabilities([Out] byte[] capabilities);

    [DllImport("powrprof.dll")]
    public static extern uint PowerGetActiveScheme(IntPtr rootPowerKey, out IntPtr activePolicyGuid);

    [DllImport("powrprof.dll")]
    public static extern uint PowerReadACValueIndex(IntPtr rootPowerKey, ref Guid scheme, ref Guid subGroup, ref Guid setting, out uint value);

    [DllImport("powrprof.dll")]
    public static extern uint PowerReadDCValueIndex(IntPtr rootPowerKey, ref Guid scheme, ref Guid subGroup, ref Guid setting, out uint value);

    [DllImport("kernel32.dll")]
    public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    [DllImport("kernel32.dll")]
    public static extern IntPtr LocalFree(IntPtr memory);

    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr RegisterPowerSettingNotification(IntPtr hWnd, ref Guid powerSetting, int flags);

    [DllImport("user32.dll")]
    public static extern IntPtr RegisterSuspendResumeNotification(IntPtr hWnd, int flags);
}
