using PowerPlan.Core;

namespace PowerPlan.Core.Tests;

public class ScheduleCalculatorTests
{
    static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    static Schedule At(int hour, int minute, Recurrence recurrence) =>
        new() { Name = "test", Action = PowerAction.Wake, Time = new TimeOnly(hour, minute), Recurrence = recurrence };

    [Fact]
    public void Daily_later_today_runs_today()
    {
        var now = new DateTimeOffset(2026, 10, 29, 6, 0, 0, TimeSpan.Zero);
        var next = ScheduleCalculator.NextOccurrence(At(7, 0, Recurrence.Daily), now, Utc);
        Assert.Equal(new DateTimeOffset(2026, 10, 29, 7, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void Daily_already_passed_runs_tomorrow()
    {
        var now = new DateTimeOffset(2026, 10, 29, 7, 0, 0, TimeSpan.Zero);
        var next = ScheduleCalculator.NextOccurrence(At(7, 0, Recurrence.Daily), now, Utc);
        Assert.Equal(new DateTimeOffset(2026, 10, 30, 7, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void Weekdays_from_friday_evening_runs_monday()
    {
        var fridayEvening = new DateTimeOffset(2026, 10, 30, 20, 0, 0, TimeSpan.Zero);
        var next = ScheduleCalculator.NextOccurrence(At(7, 0, Recurrence.Weekdays), fridayEvening, Utc);
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 7, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void Single_weekday_already_passed_today_runs_next_week()
    {
        var thursdayNoon = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
        var next = ScheduleCalculator.NextOccurrence(At(7, 0, Recurrence.Weekly(DayOfWeek.Thursday)), thursdayNoon, Utc);
        Assert.Equal(new DateTimeOffset(2026, 11, 5, 7, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void Once_in_future_runs_then_never_again()
    {
        var schedule = At(9, 30, Recurrence.Once(new DateOnly(2026, 11, 1)));
        var before = new DateTimeOffset(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
        var due = new DateTimeOffset(2026, 11, 1, 9, 30, 0, TimeSpan.Zero);

        Assert.Equal(due, ScheduleCalculator.NextOccurrence(schedule, before, Utc));
        Assert.Null(ScheduleCalculator.NextOccurrence(schedule, due, Utc));
    }

    [Fact]
    public void Disabled_schedule_never_runs()
    {
        var schedule = At(7, 0, Recurrence.Daily) with { Enabled = false };
        Assert.Null(ScheduleCalculator.NextOccurrence(schedule, DateTimeOffset.UnixEpoch, Utc));
    }

    [Fact]
    public void Time_in_spring_forward_gap_runs_just_after_the_jump()
    {
        // London clocks go 01:00 -> 02:00 on 2026-03-29, so 01:30 does not exist.
        var now = new DateTimeOffset(2026, 3, 28, 12, 0, 0, TimeSpan.Zero);
        var next = ScheduleCalculator.NextOccurrence(At(1, 30, Recurrence.Daily), now, Zones.London);
        Assert.Equal(new DateTimeOffset(2026, 3, 29, 1, 30, 0, TimeSpan.Zero), next!.Value.ToUniversalTime());
        Assert.Equal(new TimeOnly(2, 30), TimeOnly.FromDateTime(next.Value.DateTime));
    }

    [Fact]
    public void Time_repeated_when_clocks_go_back_runs_once_at_first_occurrence()
    {
        // London clocks go 02:00 -> 01:00 on 2026-10-25, so 01:30 happens twice.
        var now = new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
        var schedule = At(1, 30, Recurrence.Daily);

        var first = ScheduleCalculator.NextOccurrence(schedule, now, Zones.London)!.Value;
        Assert.Equal(new DateTimeOffset(2026, 10, 25, 0, 30, 0, TimeSpan.Zero), first.ToUniversalTime());

        var afterFirst = ScheduleCalculator.NextOccurrence(schedule, first, Zones.London)!.Value;
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 1, 30, 0, TimeSpan.Zero), afterFirst.ToUniversalTime());
    }
}
