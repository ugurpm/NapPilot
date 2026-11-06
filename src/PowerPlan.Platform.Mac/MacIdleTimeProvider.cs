using System.Runtime.InteropServices;
using PowerPlan.Core;

namespace PowerPlan.Platform.Mac;

public sealed class MacIdleTimeProvider : IIdleTimeProvider
{
    // kCGEventSourceStateHIDSystemState = 1, kCGAnyInputEventType = ~0u: the same idle time pmset and screensavers use.
    const int HidSystemState = 1;
    const uint AnyInputEventType = unchecked((uint)~0);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    static extern double CGEventSourceSecondsSinceLastEventType(int stateId, uint eventType);

    public TimeSpan IdleTime => TimeSpan.FromSeconds(CGEventSourceSecondsSinceLastEventType(HidSystemState, AnyInputEventType));
}
