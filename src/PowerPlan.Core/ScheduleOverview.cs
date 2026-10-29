namespace PowerPlan.Core;

public enum EntryKind
{
    Upcoming,
    /// <summary>Turned off by the user but would still run if turned back on.</summary>
    Paused,
    Past,
}

/// <summary>One row of the schedule list: a schedule still to come, or something that already happened.</summary>
public sealed record ScheduleEntry(EntryKind Kind, string Name, PowerAction Action, DateTimeOffset? When, Schedule? Schedule, RunRecord? Run)
{
    /// <summary>Past rows: what happened. A one-off with no run record never ran (the PC was off, or it was paused).</summary>
    public string Outcome => Run switch
    {
        null when Kind == EntryKind.Past => "Did not run",
        null => "",
        { Outcome: RunOutcome.Done } r => r.Detail ?? "Done",
        { Outcome: RunOutcome.DryRun } => "Dry-run: only logged",
        { Outcome: RunOutcome.SkippedLate } => "Skipped: the computer was off or asleep at that time",
        { Outcome: RunOutcome.Failed } r => $"Failed: {r.Detail}",
        _ => "",
    };
}

public static class ScheduleOverview
{
    /// <summary>Upcoming and paused schedules soonest first, then past runs newest first.</summary>
    public static IReadOnlyList<ScheduleEntry> Build(
        IReadOnlyList<Schedule> schedules, IReadOnlyList<RunRecord> history, DateTimeOffset now, TimeZoneInfo zone)
    {
        var coming = new List<ScheduleEntry>();
        var past = history.Select(r => new ScheduleEntry(EntryKind.Past, r.Name, r.Action, r.RanAt, null, r)).ToList();

        foreach (var s in schedules)
        {
            if (ScheduleCalculator.NextOccurrence(s, now, zone) is { } next)
                coming.Add(new(EntryKind.Upcoming, s.Name, s.Action, next, s, null));
            else if (ScheduleCalculator.NextOccurrence(s with { Enabled = true }, now, zone) is { } wouldBe)
                coming.Add(new(EntryKind.Paused, s.Name, s.Action, wouldBe, s, null));
            else if (s.Recurrence.Date is { } date && history.All(r => r.ScheduleId != s.Id))
                past.Add(new(EntryKind.Past, s.Name, s.Action, ScheduleCalculator.ToInstant(date.ToDateTime(s.Time), zone), s, null));
        }

        return
        [
            .. coming.OrderBy(e => e.Kind).ThenBy(e => e.When),
            .. past.OrderByDescending(e => e.When),
        ];
    }
}
