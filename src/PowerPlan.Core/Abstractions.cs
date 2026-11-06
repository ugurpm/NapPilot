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

/// <summary>How long the user has gone without touching the keyboard or mouse.</summary>
public interface IIdleTimeProvider
{
    TimeSpan IdleTime { get; }
}

public enum PostponeChoice
{
    /// <summary>Go ahead with the scheduled action now.</summary>
    Proceed,
    /// <summary>Check again after <see cref="PostponeDecision.PostponeBy"/>.</summary>
    Postpone,
    /// <summary>Skip today's run; the schedule still fires at its next normal occurrence.</summary>
    CancelledForToday,
}

public sealed record PostponeDecision(PostponeChoice Choice, TimeSpan? PostponeBy = null);

/// <summary>
/// Shown instead of running a schedule straight away when the user is actively using the computer, so a
/// shutdown, restart or sleep never catches them without warning. Not asked for <see cref="PowerAction.Wake"/>.
/// </summary>
public interface IActivityPrompt
{
    PostponeDecision Ask(Schedule schedule, DateTimeOffset scheduledFor);
}
