namespace PowerPlan.Core;

public sealed class ScheduleService(IScheduleStore store, IScheduleBackend backend, TimeProvider time, ILog log)
{
    DateTimeOffset Now => time.GetUtcNow();
    TimeZoneInfo Zone => time.LocalTimeZone;

    public IReadOnlyList<Schedule> All() => store.Load();

    public Schedule? Find(Guid id) => store.Load().FirstOrDefault(s => s.Id == id);

    public DateTimeOffset? NextRun(Schedule schedule) => ScheduleCalculator.NextOccurrence(schedule, Now, Zone);

    public bool HasPendingWake => SleepPolicy.HasPendingWake(store.Load(), Now, Zone);

    /// <summary>Adds or replaces a schedule and arms its next run. Returns validation errors; nothing is saved if there are any.</summary>
    public IReadOnlyList<string> Save(Schedule schedule)
    {
        var errors = ScheduleValidator.Validate(schedule, Now, Zone);
        if (errors.Count > 0) return errors;

        Replace(schedule);
        Arm(schedule);
        return [];
    }

    public void Remove(Guid id)
    {
        store.Save(store.Load().Where(s => s.Id != id).ToList());
        backend.Unregister(id);
        log.Write($"Removed schedule {id}");
    }

    /// <summary>Re-registers every schedule, e.g. at app start, so the OS scheduler matches the saved list.</summary>
    public void ArmAll()
    {
        foreach (var schedule in store.Load()) Arm(schedule);
    }

    /// <summary>After a run: one-off schedules are disabled, recurring ones are armed for their next occurrence.</summary>
    internal void Advance(Schedule schedule)
    {
        if (schedule.Recurrence.IsOnce)
        {
            // The one-time trigger cannot fire again, so the task is left alone while it is still running.
            Replace(schedule with { Enabled = false });
            return;
        }
        Arm(schedule);
    }

    void Replace(Schedule schedule) =>
        store.Save(store.Load().Where(s => s.Id != schedule.Id).Append(schedule).ToList());

    void Arm(Schedule schedule)
    {
        if (NextRun(schedule) is { } next)
        {
            backend.Register(schedule, next);
            log.Write($"Armed '{schedule.Name}' ({schedule.Action}) for {next:yyyy-MM-dd HH:mm zzz}");
        }
        else
        {
            backend.Unregister(schedule.Id);
        }
    }
}

/// <summary>Runs a schedule when the OS scheduler starts the app with <c>--run &lt;id&gt;</c>.</summary>
public sealed class ScheduledRunner(ScheduleService schedules, IPowerController power, IWakeVerifier wakeVerifier, ILog log)
{
    public bool Run(Guid id)
    {
        var schedule = schedules.Find(id);
        if (schedule is not { Enabled: true })
        {
            log.Write($"Run {id}: no enabled schedule with that id");
            return false;
        }

        log.Write($"Running '{schedule.Name}' ({schedule.Action})");
        // Arm the next occurrence first: shutdown or restart may end this process.
        schedules.Advance(schedule);

        switch (schedule.Action)
        {
            case PowerAction.Shutdown: power.Shutdown(); break;
            case PowerAction.Restart: power.Restart(); break;
            case PowerAction.Sleep: power.Sleep(); break;
            case PowerAction.Wake:
                power.TurnDisplayOn();
                log.Write($"Wake check for '{schedule.Name}': {wakeVerifier.Check(schedule.Id)}");
                break;
        }
        return true;
    }
}
