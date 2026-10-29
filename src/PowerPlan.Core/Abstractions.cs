namespace PowerPlan.Core;

public interface ILog
{
    void Write(string message);
}

public interface IPowerController
{
    void Shutdown();
    void Restart();
    /// <summary>Sleep in a way a scheduled wake can end (hibernate on Modern Standby hardware).</summary>
    void Sleep();
    void Hibernate();
    void TurnDisplayOn();
}

/// <summary>Arms the OS scheduler so a schedule runs even when the app is closed.</summary>
public interface IScheduleBackend
{
    void Register(Schedule schedule, DateTimeOffset runAt);
    void Unregister(Guid scheduleId);
}

public interface ICapabilityProbe
{
    PowerCapabilities Probe();
}

public interface IScheduleStore
{
    IReadOnlyList<Schedule> Load();
    void Save(IReadOnlyList<Schedule> schedules);
}

public interface IHistoryStore
{
    IReadOnlyList<RunRecord> Load();
    void Save(IReadOnlyList<RunRecord> records);
}

public enum WakeCheck
{
    WokeOnSchedule,
    AlreadyAwake,
    OtherWakeSource,
    Unknown,
}

/// <summary>Confirms after a wake run that the PC was actually woken by its timer.</summary>
public interface IWakeVerifier
{
    WakeCheck Check(Guid scheduleId);
}

/// <summary>Starts the tray app (<c>--tray</c>) when the user signs in, so wake handling and update checks keep running.</summary>
public interface ILoginItem
{
    bool IsEnabled { get; }
    void SetEnabled(bool enabled);
}
