using System.Runtime.InteropServices;
using PowerPlan.Core;
using static PowerPlan.Platform.Windows.NativeMethods;

namespace PowerPlan.Platform.Windows;

public sealed class WindowsCapabilityProbe : ICapabilityProbe
{
    // Byte offsets in SYSTEM_POWER_CAPABILITIES (all BOOLEAN fields up to AoAc).
    const int SystemS3 = 5, SystemS4 = 6, HiberFilePresent = 8, AoAc = 20;

    public PowerCapabilities Probe()
    {
        var caps = new byte[128];
        GetPwrCapabilities(caps);
        GetSystemPowerStatus(out var status);

        return new PowerCapabilities(
            ModernStandby: caps[AoAc] != 0,
            S3Sleep: caps[SystemS3] != 0,
            Hibernate: caps[SystemS4] != 0 && caps[HiberFilePresent] != 0,
            WakeTimersOnAc: ReadWakeTimerSetting(ac: true),
            WakeTimersOnBattery: ReadWakeTimerSetting(ac: false),
            OnAcPower: status.ACLineStatus == 1);
    }

    /// <summary>"Allow wake timers": 0 off, 1 on, 2 important only (system timers, which excludes ours).</summary>
    static bool ReadWakeTimerSetting(bool ac)
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var schemePtr) != 0) return false;
        try
        {
            var scheme = Marshal.PtrToStructure<Guid>(schemePtr);
            var subGroup = GUID_SLEEP_SUBGROUP;
            var setting = GUID_ALLOW_RTC_WAKE;
            var result = ac
                ? PowerReadACValueIndex(IntPtr.Zero, ref scheme, ref subGroup, ref setting, out var value)
                : PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref subGroup, ref setting, out value);
            return result == 0 && value == 1;
        }
        finally
        {
            LocalFree(schemePtr);
        }
    }
}
