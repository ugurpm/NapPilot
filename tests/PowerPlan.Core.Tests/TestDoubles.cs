using PowerPlan.Core;

namespace PowerPlan.Core.Tests;

sealed class FixedTime(DateTimeOffset now, TimeZoneInfo zone) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
    public override TimeZoneInfo LocalTimeZone => zone;
}

sealed class MemoryStore : IScheduleStore
{
    public List<Schedule> Items { get; } = [];
    public IReadOnlyList<Schedule> Load() => Items.ToList();
    public void Save(IReadOnlyList<Schedule> schedules) { Items.Clear(); Items.AddRange(schedules); }
}

sealed class RecordingBackend : IScheduleBackend
{
    public Dictionary<Guid, DateTimeOffset> Registered { get; } = [];
    public void Register(Schedule schedule, DateTimeOffset runAt) => Registered[schedule.Id] = runAt;
    public void Unregister(Guid scheduleId) => Registered.Remove(scheduleId);
}

sealed class RecordingPower : IPowerController
{
    public List<string> Calls { get; } = [];
    public void Shutdown() => Calls.Add(nameof(Shutdown));
    public void Restart() => Calls.Add(nameof(Restart));
    public void Sleep() => Calls.Add(nameof(Sleep));
    public void Hibernate() => Calls.Add(nameof(Hibernate));
    public void TurnDisplayOn() => Calls.Add(nameof(TurnDisplayOn));
}

sealed class FixedWakeVerifier(WakeCheck result) : IWakeVerifier
{
    public WakeCheck Check(Guid scheduleId) => result;
}

sealed class ListLog : ILog
{
    public List<string> Lines { get; } = [];
    public void Write(string message) => Lines.Add(message);
}

static class Zones
{
    public static readonly TimeZoneInfo London = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
}
