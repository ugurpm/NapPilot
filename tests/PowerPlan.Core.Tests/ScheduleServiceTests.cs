using PowerPlan.Core;

namespace PowerPlan.Core.Tests;

public class ScheduleServiceTests
{
    readonly MemoryStore store = new();
    readonly MemoryHistory history = new();
    readonly RecordingBackend backend = new();
    readonly RecordingPower power = new();
    readonly ListLog log = new();
    readonly FixedTime time = new(new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc);
    readonly ScheduleService service;

    public ScheduleServiceTests() => service = new ScheduleService(store, history, backend, time, log);

    ScheduledRunner Runner(WakeCheck check = WakeCheck.WokeOnSchedule, bool dryRun = false) =>
        new(service, power, new FixedWakeVerifier(check), () => dryRun, log);

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
    public void Run_is_recorded_in_history()
    {
        var schedule = new Schedule { Name = "Night", Action = PowerAction.Shutdown, Time = new TimeOnly(23, 0) };
        service.Save(schedule);

        Runner(dryRun: true).Run(schedule.Id);

        var record = history.Items.Single();
        Assert.Equal((schedule.Id, "Night", PowerAction.Shutdown, RunOutcome.DryRun), (record.ScheduleId, record.Name, record.Action, record.Outcome));
    }

    [Fact]
    public void Wake_record_says_what_woke_the_computer()
    {
        var schedule = new Schedule { Name = "Morning", Action = PowerAction.Wake, Time = new TimeOnly(7, 0) };
        service.Save(schedule);

        Runner(WakeCheck.OtherWakeSource).Run(schedule.Id);

        Assert.Equal("Something else woke the computer", history.Items.Single().Detail);
    }

    [Fact]
    public void Late_shutdown_is_skipped_but_still_re_armed()
    {
        var schedule = new Schedule { Name = "Night", Action = PowerAction.Shutdown, Time = new TimeOnly(23, 0) };
        service.Save(schedule);
        var due = new DateTimeOffset(2026, 10, 29, 23, 0, 0, TimeSpan.Zero);
        time.Now = due.AddHours(8);

        Assert.True(Runner().Run(schedule.Id, due));

        Assert.Empty(power.Calls);
        Assert.Equal(RunOutcome.SkippedLate, history.Items.Single().Outcome);
        Assert.Equal(new DateTimeOffset(2026, 10, 30, 23, 0, 0, TimeSpan.Zero), backend.Registered[schedule.Id]);
    }

    [Fact]
    public void Slightly_late_shutdown_still_runs()
    {
        var schedule = new Schedule { Name = "Night", Action = PowerAction.Shutdown, Time = new TimeOnly(23, 0) };
        service.Save(schedule);
        var due = new DateTimeOffset(2026, 10, 29, 23, 0, 0, TimeSpan.Zero);
        time.Now = due.AddMinutes(1);

        Runner().Run(schedule.Id, due);

        Assert.Equal(["Shutdown"], power.Calls);
    }

    [Fact]
    public void Late_wake_still_turns_the_screen_on()
    {
        var schedule = new Schedule { Name = "Morning", Action = PowerAction.Wake, Time = new TimeOnly(7, 0) };
        service.Save(schedule);
        var due = new DateTimeOffset(2026, 10, 30, 7, 0, 0, TimeSpan.Zero);
        time.Now = due.AddHours(1);

        Runner().Run(schedule.Id, due);

        Assert.Equal(["TurnDisplayOn"], power.Calls);
    }

    [Fact]
    public void Failed_action_replaces_its_record()
    {
        var schedule = new Schedule { Name = "Night", Action = PowerAction.Shutdown, Time = new TimeOnly(23, 0) };
        service.Save(schedule);

        Assert.Throws<InvalidOperationException>(() =>
            new ScheduledRunner(service, new ThrowingPower(), new FixedWakeVerifier(WakeCheck.Unknown), () => false, log).Run(schedule.Id));

        var record = history.Items.Single();
        Assert.Equal(RunOutcome.Failed, record.Outcome);
        Assert.Equal("boom", record.Detail);
    }

    [Fact]
    public void Editing_a_schedule_replaces_it_and_re_arms()
    {
        var schedule = new Schedule { Name = "Night", Action = PowerAction.Shutdown, Time = new TimeOnly(23, 0) };
        service.Save(schedule);

        Assert.Empty(service.Save(schedule with { Name = "Late", Time = new TimeOnly(23, 30) }));

        Assert.Equal("Late", store.Items.Single().Name);
        Assert.Equal(new DateTimeOffset(2026, 10, 29, 23, 30, 0, TimeSpan.Zero), backend.Registered[schedule.Id]);
    }

    [Fact]
    public void Pausing_unregisters_and_resuming_re_arms()
    {
        var schedule = new Schedule { Name = "Night", Action = PowerAction.Shutdown, Time = new TimeOnly(23, 0) };
        service.Save(schedule);

        service.SetEnabled(schedule.Id, false);
        Assert.Empty(backend.Registered);
        Assert.Equal(EntryKind.Paused, service.Overview().Single().Kind);

        service.SetEnabled(schedule.Id, true);
        Assert.Single(backend.Registered);
    }

    [Fact]
    public void Run_still_happens_when_re_arming_fails()
    {
        var schedule = new Schedule { Name = "Night", Action = PowerAction.Shutdown, Time = new TimeOnly(23, 0) };
        store.Items.Add(schedule);
        var refusing = new ScheduleService(store, history, new ThrowingBackend(), time, log);

        Assert.True(new ScheduledRunner(refusing, power, new FixedWakeVerifier(WakeCheck.Unknown), () => false, log).Run(schedule.Id));

        Assert.Equal(["Shutdown"], power.Calls);
        Assert.Contains(log.Lines, l => l.Contains("running it anyway"));
    }

    [Fact]
    public void Failed_registration_saves_nothing()
    {
        var failing = new ScheduleService(store, history, new ThrowingBackend(), time, log);

        Assert.Throws<InvalidOperationException>(() => failing.Save(new Schedule { Name = "x", Time = new TimeOnly(7, 0) }));

        Assert.Empty(store.Items);
    }

    [Fact]
    public void Removing_the_last_record_of_a_finished_one_off_removes_the_schedule()
    {
        var schedule = new Schedule
        {
            Name = "x", Action = PowerAction.Shutdown, Time = new TimeOnly(23, 0),
            Recurrence = Recurrence.Once(new DateOnly(2026, 10, 29)),
        };
        service.Save(schedule);
        time.Now = new DateTimeOffset(2026, 10, 29, 23, 0, 0, TimeSpan.Zero);
        Runner().Run(schedule.Id);

        service.RemovePast(service.Overview().Single());

        Assert.Empty(history.Items);
        Assert.Empty(store.Items);
    }

    [Fact]
    public void Removing_a_past_run_keeps_the_recurring_schedule()
    {
        var schedule = new Schedule { Name = "Night", Action = PowerAction.Shutdown, Time = new TimeOnly(23, 0) };
        service.Save(schedule);
        Runner().Run(schedule.Id);

        service.RemovePast(service.Overview().Single(e => e.Kind == EntryKind.Past));

        Assert.Empty(history.Items);
        Assert.Single(store.Items);
    }

    [Fact]
    public void Clear_past_keeps_upcoming()
    {
        var recurring = new Schedule { Name = "Night", Action = PowerAction.Shutdown, Time = new TimeOnly(23, 0) };
        service.Save(recurring);
        Runner().Run(recurring.Id);

        service.ClearPast();

        Assert.Equal(EntryKind.Upcoming, service.Overview().Single().Kind);
    }

    [Fact]
    public void History_is_capped()
    {
        for (var i = 0; i < ScheduleService.HistoryLimit + 5; i++)
            service.Record(new RunRecord { Name = $"r{i}", RanAt = time.Now.AddMinutes(i) });

        Assert.Equal(ScheduleService.HistoryLimit, history.Items.Count);
        Assert.Equal("r5", history.Items[0].Name);
    }

    [Fact]
    public void Pending_wake_reflects_saved_schedules()
    {
        Assert.False(service.HasPendingWake);
        service.Save(new Schedule { Name = "w", Action = PowerAction.Wake, Time = new TimeOnly(7, 0) });
        Assert.True(service.HasPendingWake);
    }
}

sealed class ThrowingPower : IPowerController
{
    public void Shutdown() => throw new InvalidOperationException("boom");
    public void Restart() => throw new InvalidOperationException("boom");
    public void Sleep() => throw new InvalidOperationException("boom");
    public void Hibernate() => throw new InvalidOperationException("boom");
    public void TurnDisplayOn() => throw new InvalidOperationException("boom");
}

sealed class ThrowingBackend : IScheduleBackend
{
    public void Register(Schedule schedule, DateTimeOffset runAt) => throw new InvalidOperationException("refused");
    public void Unregister(Guid scheduleId) { }
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
    public void Round_trips_history()
    {
        var store = new JsonHistoryStore(Path.Combine(dir, "history.json"));
        var record = new RunRecord
        {
            ScheduleId = Guid.NewGuid(), Name = "Night", Action = PowerAction.Shutdown, Outcome = RunOutcome.SkippedLate,
            ScheduledFor = new DateTimeOffset(2026, 10, 29, 23, 0, 0, TimeSpan.FromHours(3)),
            RanAt = new DateTimeOffset(2026, 10, 30, 7, 0, 0, TimeSpan.FromHours(3)),
        };

        store.Save([record]);

        Assert.Equal(record, store.Load().Single());
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
