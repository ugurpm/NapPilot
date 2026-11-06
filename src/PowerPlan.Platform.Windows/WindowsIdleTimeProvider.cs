using PowerPlan.Core;
using static PowerPlan.Platform.Windows.NativeMethods;

namespace PowerPlan.Platform.Windows;

public sealed class WindowsIdleTimeProvider : IIdleTimeProvider
{
    public TimeSpan IdleTime
    {
        get
        {
            var info = new LASTINPUTINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<LASTINPUTINFO>() };
            // TickCount wraps every ~49.7 days; unsigned subtraction handles the wrap the same way Windows does.
            // If the call fails, assume active rather than silently skipping the activity check.
            return GetLastInputInfo(ref info) ? TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime)) : TimeSpan.Zero;
        }
    }
}
