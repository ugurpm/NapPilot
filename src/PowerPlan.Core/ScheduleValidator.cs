namespace PowerPlan.Core;

public static class ScheduleValidator
{
    public static IReadOnlyList<string> Validate(Schedule schedule, DateTimeOffset now, TimeZoneInfo zone)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(schedule.Name))
            errors.Add("Give the schedule a name.");

        if (!schedule.Recurrence.IsOnce && schedule.Recurrence.Days.Length == 0)
            errors.Add("Pick at least one day.");
        else if (schedule.Recurrence.IsOnce && ScheduleCalculator.NextOccurrence(schedule with { Enabled = true }, now, zone) is null)
            errors.Add("That time has already passed.");

        return errors;
    }
}
