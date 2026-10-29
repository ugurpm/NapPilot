namespace PowerPlan.Core;

public sealed class ScheduleService(IScheduleStore store, IHistoryStore history, IScheduleBackend backend, TimeProvider time, ILog log)
{
    /// <summary>Oldest records are dropped beyond this, so the history file stays small.</summary>
    public const int HistoryLimit = 500;

    public DateTimeOffset Now => time.GetUtcNow();
    TimeZoneInfo Zone => time.LocalTimeZone;

    public IReadOnlyList<Schedule> All() => store.Load();

    public IReadOnlyList<RunRecord> History() => history.Load();

    public Schedule? Find(Guid id) => store.Load().FirstOrDefault(s => s.Id == id);

    public DateTimeOffset? NextRun(Schedule schedule) => ScheduleCalculator.NextOccurrence(schedule, Now, Zone);

    public bool HasPendingWake => SleepPolicy.HasPendingWake(store.Load(), Now, Zone);

    public IReadOnlyList<ScheduleEntry> Overview() => ScheduleOverview.Build(store.Load(), history.Load(), Now, Zone);

    /// <summary>Adds or replaces a schedule and arms its next run. Returns validation errors; nothing is saved if there are any.</summary>
    public IReadOnlyList<string> Save(Schedule schedule)
    {
        var errors = ScheduleValidator.Validate(schedule, Now, Zone);
        if (errors.Count > 0) return errors;

        // Arm first: if the OS refuses (e.g. the user cancels the admin prompt on macOS) nothing is saved.
        Arm(schedule);
        Replace(schedule);
        return [];
    }

    public IReadOnlyList<string> SetEnabled(Guid id, bool enabled) =>
        Find(id) is { } schedule ? Save(schedule with { Enabled = enabled }) : ["That schedule no longer exists."];

    public void Remove(Guid id)
    {
        backend.Unregister(id);
        store.Save(store.Load().Where(s => s.Id != id).ToList());
        log.Write($"Removed schedule {id}");
    }

    /// <summary>Deletes a past entry. A finished one-off schedule goes with its last record, since nothing else shows it.</summary>
    public void RemovePast(ScheduleEntry entry)
    {
        if (entry.Run is { } run)
        {
            var remaining = history.Load().Where(r => r.Id != run.Id).ToList();
            history.Save(remaining);
            if (Find(run.ScheduleId) is { } schedule && IsFinishedOnce(schedule) && remaining.All(r => r.ScheduleId != schedule.Id))
                Remove(schedule.Id);
        }
        else if (entry.Schedule is { } schedule)
        {
            Remove(schedule.Id);
        }
    }

    public void ClearPast()
    {
        foreach (var entry in Overview().Where(e => e.Kind == EntryKind.Past)) RemovePast(entry);
    }

    /// <summary>Adds a run record, or replaces the one with the same id (a run that failed after it was recorded).</summary>
    public void Record(RunRecord record)
    {
        var records = history.Load().Where(r => r.Id != record.Id).Append(record).OrderBy(r => r.RanAt).ToList();
        history.Save(records.Skip(Math.Max(0, records.Count - HistoryLimit)).ToList());
    }

    /// <summary>Re-registers every schedule, e.g. at app start, so the OS scheduler matches the saved list.</summary>
    public void ArmAll()
    {
        foreach (var schedule in store.Load())
        {
            try
            {
                Arm(schedule);
            }
            catch (Exception e)
            {
                // One schedule the OS refuses must not stop the others from being armed.
                log.Write($"Arming '{schedule.Name}' failed: {e.Message}");
            }
        }
    }

    public void UnregisterAll()
    {
        foreach (var schedule in store.Load()) backend.Unregister(schedule.Id);
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

    bool IsFinishedOnce(Schedule schedule) =>
        schedule.Recurrence.IsOnce && ScheduleCalculator.NextOccurrence(schedule with { Enabled = true }, Now, Zone) is null;

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

/// <summary>Runs a schedule when the OS scheduler starts the app with <c>--run &lt;id&gt; [--at &lt;unix seconds&gt;]</c>.</summary>
public sealed class ScheduledRunner(
    ScheduleService schedules, IPowerController power, IWakeVerifier wakeVerifier, Func<bool> dryRun, ILog log)
{
    /// <summary>
    /// launchd starts missed jobs as soon as the Mac wakes. A shutdown, restart or sleep that late would surprise
    /// the user, so it is skipped; Task Scheduler never starts late because StartWhenAvailable is off.
    /// </summary>
    public static readonly TimeSpan LateLimit = TimeSpan.FromMinutes(5);

    public bool Run(Guid id, DateTimeOffset? scheduledFor = null)
    {
        var schedule = schedules.Find(id);
        if (schedule is not { Enabled: true })
        {
            log.Write($"Run {id}: no enabled schedule with that id");
            return false;
        }

        var record = new RunRecord
        {
            ScheduleId = schedule.Id,
            Name = schedule.Name,
            Action = schedule.Action,
            ScheduledFor = scheduledFor,
            RanAt = schedules.Now,
        };

        // Arm the next occurrence first: shutdown or restart may end this process. If the OS refuses (on macOS an
        // admin prompt nobody answers at night), this run must still happen; the app re-arms at its next start.
        try
        {
            schedules.Advance(schedule);
        }
        catch (Exception e)
        {
            log.Write($"Re-arming '{schedule.Name}' failed, running it anyway: {e.Message}");
        }

        if (schedule.Action != PowerAction.Wake && scheduledFor is { } due && schedules.Now - due > LateLimit)
        {
            log.Write($"Skipping '{schedule.Name}' ({schedule.Action}): due {due:yyyy-MM-dd HH:mm zzz}, started too late");
            schedules.Record(record with { Outcome = RunOutcome.SkippedLate });
            return true;
        }

        log.Write($"Running '{schedule.Name}' ({schedule.Action})");
        // Record before acting: after a shutdown starts there may be no time left to write it.
        var outcome = dryRun() ? RunOutcome.DryRun : RunOutcome.Done;
        if (schedule.Action != PowerAction.Wake) schedules.Record(record with { Outcome = outcome });

        try
        {
            switch (schedule.Action)
            {
                case PowerAction.Shutdown: power.Shutdown(); break;
                case PowerAction.Restart: power.Restart(); break;
                case PowerAction.Sleep: power.Sleep(); break;
                case PowerAction.Wake:
                    power.TurnDisplayOn();
                    var check = wakeVerifier.Check(schedule.Id);
                    log.Write($"Wake check for '{schedule.Name}': {check}");
                    schedules.Record(record with { Outcome = outcome, Detail = Describe(check) });
                    break;
            }
        }
        catch (Exception e)
        {
            schedules.Record(record with { Outcome = RunOutcome.Failed, Detail = e.Message });
            throw;
        }
        return true;
    }

    static string Describe(WakeCheck check) => check switch
    {
        WakeCheck.WokeOnSchedule => "Woke the computer on schedule",
        WakeCheck.AlreadyAwake => "The computer was already awake",
        WakeCheck.OtherWakeSource => "Something else woke the computer",
        _ => "Could not confirm what woke the computer",
    };
}
