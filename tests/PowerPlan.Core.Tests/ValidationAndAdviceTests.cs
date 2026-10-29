using PowerPlan.Core;

namespace PowerPlan.Core.Tests;

public class ScheduleValidatorTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Valid_daily_schedule_has_no_errors()
    {
        var schedule = new Schedule { Name = "Morning", Time = new TimeOnly(7, 0) };
        Assert.Empty(ScheduleValidator.Validate(schedule, Now, TimeZoneInfo.Utc));
    }

    [Fact]
    public void Missing_name_is_rejected()
    {
        var schedule = new Schedule { Name = " ", Time = new TimeOnly(7, 0) };
        Assert.Contains("Give the schedule a name.", ScheduleValidator.Validate(schedule, Now, TimeZoneInfo.Utc));
    }

    [Fact]
    public void Weekly_without_days_is_rejected()
    {
        var schedule = new Schedule { Name = "x", Recurrence = Recurrence.Weekly() };
        Assert.Contains("Pick at least one day.", ScheduleValidator.Validate(schedule, Now, TimeZoneInfo.Utc));
    }

    [Fact]
    public void Once_in_the_past_is_rejected()
    {
        var schedule = new Schedule { Name = "x", Time = new TimeOnly(11, 0), Recurrence = Recurrence.Once(new DateOnly(2026, 10, 29)) };
        Assert.Contains("That time has already passed.", ScheduleValidator.Validate(schedule, Now, TimeZoneInfo.Utc));
    }
}

public class WakeAdvisorTests
{
    // The development laptop as measured on 2026-10-29: Modern Standby only, wake timers on AC only.
    static readonly PowerCapabilities DevLaptop = new(
        ModernStandby: true, S3Sleep: false, Hibernate: true, WakeTimersOnAc: true, WakeTimersOnBattery: false, OnAcPower: true);

    [Fact]
    public void Modern_standby_with_hibernate_can_wake_with_notes()
    {
        var result = WakeAdvisor.Assess(DevLaptop);
        Assert.True(result.CanWake);
        Assert.Contains(result.Notes, n => n.Contains("hibernate"));
        Assert.Contains(result.Notes, n => n.Contains("plugged in"));
    }

    [Fact]
    public void On_battery_now_is_called_out()
    {
        var result = WakeAdvisor.Assess(DevLaptop with { OnAcPower = false });
        Assert.Contains("It is on battery right now.", result.Notes);
    }

    [Fact]
    public void Modern_standby_without_hibernate_cannot_wake()
    {
        Assert.False(WakeAdvisor.Assess(DevLaptop with { Hibernate = false }).CanWake);
    }

    [Fact]
    public void Wake_timers_off_everywhere_cannot_wake()
    {
        Assert.False(WakeAdvisor.Assess(DevLaptop with { WakeTimersOnAc = false }).CanWake);
    }

    [Fact]
    public void Classic_s3_desktop_needs_no_hibernate()
    {
        var desktop = new PowerCapabilities(false, true, false, true, true, true);
        var result = WakeAdvisor.Assess(desktop);
        Assert.True(result.CanWake);
        Assert.Empty(result.Notes);
    }
}

public class SleepPolicyTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Enabled_wake_schedule_is_pending()
    {
        Schedule[] schedules = [new() { Name = "w", Action = PowerAction.Wake, Time = new TimeOnly(7, 0) }];
        Assert.True(SleepPolicy.HasPendingWake(schedules, Now, TimeZoneInfo.Utc));
    }

    [Fact]
    public void Disabled_or_non_wake_schedules_are_not_pending()
    {
        Schedule[] schedules =
        [
            new() { Name = "w", Action = PowerAction.Wake, Time = new TimeOnly(7, 0), Enabled = false },
            new() { Name = "s", Action = PowerAction.Shutdown, Time = new TimeOnly(23, 0) },
        ];
        Assert.False(SleepPolicy.HasPendingWake(schedules, Now, TimeZoneInfo.Utc));
    }
}
