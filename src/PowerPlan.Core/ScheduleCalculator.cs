namespace PowerPlan.Core;

public static class ScheduleCalculator
{
    /// <summary>The first time strictly after <paramref name="after"/> that the schedule should run, or null if it never will.</summary>
    public static DateTimeOffset? NextOccurrence(Schedule schedule, DateTimeOffset after, TimeZoneInfo zone)
    {
        if (!schedule.Enabled) return null;

        if (schedule.Recurrence.Date is { } date)
        {
            var once = ToInstant(date.ToDateTime(schedule.Time), zone);
            return once > after ? once : null;
        }

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(after, zone).DateTime);
        // Eight days so a weekly slot that already passed today is found again next week.
        for (var i = 0; i <= 7; i++)
        {
            var day = today.AddDays(i);
            if (!schedule.Recurrence.Days.Contains(day.DayOfWeek)) continue;
            var candidate = ToInstant(day.ToDateTime(schedule.Time), zone);
            if (candidate > after) return candidate;
        }
        return null;
    }

    /// <summary>
    /// Converts a wall-clock time to an instant. A time inside a DST gap runs just after the clocks jump;
    /// a time that occurs twice when clocks go back runs at the first occurrence.
    /// </summary>
    public static DateTimeOffset ToInstant(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

        if (zone.IsInvalidTime(local))
        {
            var offsetBeforeGap = zone.GetUtcOffset(local.AddHours(-12));
            return TimeZoneInfo.ConvertTime(new DateTimeOffset(local, offsetBeforeGap), zone);
        }
        if (zone.IsAmbiguousTime(local))
            return new DateTimeOffset(local, zone.GetAmbiguousTimeOffsets(local).Max());

        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}
