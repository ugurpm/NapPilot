namespace PowerPlan.Core;

/// <summary>What the hardware and Windows power settings allow.</summary>
public sealed record PowerCapabilities(
    bool ModernStandby,
    bool S3Sleep,
    bool Hibernate,
    bool WakeTimersOnAc,
    bool WakeTimersOnBattery,
    bool OnAcPower);

public sealed record WakeAssessment(bool CanWake, IReadOnlyList<string> Notes);

public static class WakeAdvisor
{
    public static WakeAssessment Assess(PowerCapabilities caps)
    {
        if (!caps.WakeTimersOnAc && !caps.WakeTimersOnBattery)
            return new(false, ["Wake timers are turned off in Windows power settings (\"Allow wake timers\")."]);

        var notes = new List<string>();
        if (!caps.S3Sleep)
        {
            // Tested on Modern Standby hardware: timers fire from hibernate but not from standby.
            if (!caps.Hibernate)
                return new(false, ["This PC can only be woken on a timer from hibernate, and hibernate is turned off."]);
            notes.Add("While a wake is scheduled, PowerPlan turns sleep into hibernate so the wake can fire. Keep PowerPlan running in the tray.");
        }

        if (!caps.WakeTimersOnBattery)
        {
            notes.Add("Wake timers are off on battery: keep the PC plugged in.");
            if (!caps.OnAcPower) notes.Add("The PC is on battery right now.");
        }
        else if (!caps.WakeTimersOnAc)
        {
            notes.Add("Wake timers are off when plugged in: wake only works on battery.");
        }

        return new(true, notes);
    }
}

public static class SleepPolicy
{
    /// <summary>True when an enabled wake schedule is still to come, so sleep must be wake-capable.</summary>
    public static bool HasPendingWake(IEnumerable<Schedule> schedules, DateTimeOffset now, TimeZoneInfo zone) =>
        schedules.Any(s => s.Action == PowerAction.Wake && ScheduleCalculator.NextOccurrence(s, now, zone) is not null);
}
