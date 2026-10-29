using PowerPlan.Core;

namespace PowerPlan.Core.Tests;

public class ScheduleServiceTests
{
    readonly MemoryStore store = new();
    readonly RecordingBackend backend = new();
    readonly RecordingPower power = new();
    readonly ListLog log = new();
    readonly FixedTime time = new(new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc);
    readonly ScheduleService service;

    public ScheduleServiceTests() => service = new ScheduleService(store, backend, time, log);

    ScheduledRunner Runner(WakeCheck check = WakeCheck.WokeOnSchedule) =>
        new(service, power, new FixedWakeVerifier(check), log);

    [Fact]
    public void Save_stores_and_arms_next_run()
    {
        var schedule = new Schedule { Name = "Morning", Action = PowerAction.Wake, Time = new TimeOnly(7, 0) };

        Assert.Empty(service.Save(schedule));
        Assert.Single(store.Items);
        Assert.Equal(new DateTimeOffset(2026, 10, 30, 7, 0, 0, TimeSpan.Zero), backend.Registered[schedule.Id]);
    }

    [Fact]
    public void Invalid_schedule_is_not_saved_or_armed()
    {
        var errors = service.Save(new Schedule { Name = "" });

        Assert.NotEmpty(errors);
        Assert.Empty(store.Items);
        Assert.Empty(backend.Registered);
    }

    [Fact]
    public void Remove_deletes_and_unregisters()
    {
        var schedule = new Schedule { Name = "x", Time = new TimeOnly(7, 0) };
        service.Save(schedule);

        service.Remove(schedule.Id);

        Assert.Empty(store.Items);
        Assert.Empty(backend.Registered);
    }

    [Fact]
    public void Wake_run_turns_display_on_and_arms_next_day()
    {
        var schedule = new Schedule { Name = "Morning", Action = PowerAction.Wake, Time = new TimeOnly(7, 0) };
        service.Save(schedule);
        time.Now = new DateTimeOffset(2026, 10, 30, 7, 0, 0, TimeSpan.Zero);

        Assert.True(Runner().Run(schedule.Id));

        Assert.Equal(["TurnDisplayOn"], power.Calls);
        Assert.Equal(new DateTimeOffset(2026, 10, 31, 7, 0, 0, TimeSpan.Zero), backend.Registered[schedule.Id]);
        Assert.Contains(log.Lines, l => l.Contains(nameof(WakeCheck.WokeOnSchedule)));
    }

    [Theory]
    [InlineData(PowerAction.Shutdown, "Shutdown")]
    [InlineData(PowerAction.Restart, "Restart")]
    [InlineData(PowerAction.Sleep, "Sleep")]
    public void Run_performs_the_action(PowerAction action, string expectedCall)
    {
        var schedule = new Schedule { Name = "x", Action = action, Time = new TimeOnly(23, 0) };
        service.Save(schedule);

        Runner().Run(schedule.Id);

        Assert.Equal([expectedCall], power.Calls);
    }

    [Fact]
    public void One_off_run_disables_the_schedule()
    {
        var schedule = new Schedule
        {
            Name = "x", Action = PowerAction.Shutdown, Time = new TimeOnly(23, 0),
            Recurrence = Recurrence.Once(new DateOnly(2026, 10, 29)),
        };
        service.Save(schedule);
        time.Now = new DateTimeOffset(2026, 10, 29, 23, 0, 0, TimeSpan.Zero);

        Runner().Run(schedule.Id);

        Assert.False(store.Items.Single().Enabled);
        Assert.False(Runner().Run(schedule.Id));
        Assert.Single(power.Calls);
    }

    [Fact]
    public void Unknown_id_does_nothing()
    {
        Assert.False(Runner().Run(Guid.NewGuid()));
        Assert.Empty(power.Calls);
    }

    [Fact]
    public void Pending_wake_reflects_saved_schedules()
    {
        Assert.False(service.HasPendingWake);
        service.Save(new Schedule { Name = "w", Action = PowerAction.Wake, Time = new TimeOnly(7, 0) });
        Assert.True(service.HasPendingWake);
    }
}

public class JsonScheduleStoreTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "powerplan-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Round_trips_schedules()
    {
        var store = new JsonScheduleStore(Path.Combine(dir, "schedules.json"));
        var once = new Schedule { Name = "Once", Action = PowerAction.Restart, Time = new TimeOnly(22, 15), Recurrence = Recurrence.Once(new DateOnly(2026, 11, 2)) };
        var weekly = new Schedule { Name = "Weekdays", Action = PowerAction.Wake, Time = new TimeOnly(7, 0), Recurrence = Recurrence.Weekdays };

        store.Save([once, weekly]);
        var loaded = store.Load();

        Assert.Equal(2, loaded.Count);
        Assert.Equal(once.Recurrence.Date, loaded[0].Recurrence.Date);
        Assert.Equal(weekly.Recurrence.Days, loaded[1].Recurrence.Days);
        Assert.Equal(weekly with { Recurrence = loaded[1].Recurrence }, loaded[1]);
    }

    [Fact]
    public void Missing_file_loads_empty()
    {
        Assert.Empty(new JsonScheduleStore(Path.Combine(dir, "none.json")).Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }
}
