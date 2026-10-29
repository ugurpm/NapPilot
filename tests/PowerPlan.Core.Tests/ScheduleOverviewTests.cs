using PowerPlan.Core;

namespace PowerPlan.Core.Tests;

public class ScheduleOverviewTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    static IReadOnlyList<ScheduleEntry> Build(IReadOnlyList<Schedule> schedules, params RunRecord[] history) =>
        ScheduleOverview.Build(schedules, history, Now, TimeZoneInfo.Utc);

    [Fact]
    public void Upcoming_come_first_soonest_first_then_paused()
    {
        var late = new Schedule { Name = "late", Time = new TimeOnly(23, 0) };
        var early = new Schedule { Name = "early", Time = new TimeOnly(13, 0) };
        var paused = new Schedule { Name = "paused", Time = new TimeOnly(12, 30), Enabled = false };

        var entries = Build([late, paused, early]);

        Assert.Equal(["early", "late", "paused"], entries.Select(e => e.Name));
        Assert.Equal([EntryKind.Upcoming, EntryKind.Upcoming, EntryKind.Paused], entries.Select(e => e.Kind));
        Assert.Equal(new DateTimeOffset(2026, 10, 29, 13, 0, 0, TimeSpan.Zero), entries[0].When);
    }

    [Fact]
    public void Past_runs_come_last_newest_first()
    {
        var older = new RunRecord { Name = "older", RanAt = Now.AddDays(-2) };
        var newer = new RunRecord { Name = "newer", RanAt = Now.AddDays(-1) };

        var entries = Build([], older, newer);

        Assert.Equal(["newer", "older"], entries.Select(e => e.Name));
        Assert.All(entries, e => Assert.Equal(EntryKind.Past, e.Kind));
    }

    [Fact]
    public void One_off_that_never_ran_shows_as_past()
    {
        var missed = new Schedule
        {
            Name = "missed", Action = PowerAction.Restart, Time = new TimeOnly(9, 0),
            Recurrence = Recurrence.Once(new DateOnly(2026, 10, 28)),
        };

        var entry = Build([missed]).Single();

        Assert.Equal(EntryKind.Past, entry.Kind);
        Assert.Equal("Did not run", entry.Outcome);
        Assert.Same(missed, entry.Schedule);
    }

    [Fact]
    public void One_off_that_ran_shows_only_its_record()
    {
        var once = new Schedule
        {
            Name = "once", Time = new TimeOnly(9, 0), Enabled = false,
            Recurrence = Recurrence.Once(new DateOnly(2026, 10, 28)),
        };
        var run = new RunRecord { ScheduleId = once.Id, Name = "once", RanAt = Now.AddDays(-1) };

        var entry = Build([once], run).Single();

        Assert.Same(run, entry.Run);
        Assert.Equal("Done", entry.Outcome);
    }

    [Theory]
    [InlineData(RunOutcome.DryRun, "Dry-run: only logged")]
    [InlineData(RunOutcome.SkippedLate, "Skipped: the computer was off or asleep at that time")]
    public void Outcome_text(RunOutcome outcome, string expected)
    {
        var entry = Build([], new RunRecord { Outcome = outcome, RanAt = Now }).Single();
        Assert.Equal(expected, entry.Outcome);
    }
}
