using System.Diagnostics.Eventing.Reader;
using PowerPlan.Core;

namespace PowerPlan.Platform.Windows;

/// <summary>
/// Reads the latest "returned from a low power state" event. When Task Scheduler woke the PC,
/// its text names the task, e.g. "...execute 'NT TASK\PowerPlan-…' scheduled task that requested waking the computer".
/// </summary>
public sealed class WindowsWakeVerifier(ILog log) : IWakeVerifier
{
    static readonly TimeSpan Lookback = TimeSpan.FromMinutes(10);

    public WakeCheck Check(Guid scheduleId)
    {
        try
        {
            var query = new EventLogQuery("System", PathType.LogName,
                "*[System[Provider[@Name='Microsoft-Windows-Power-Troubleshooter'] and EventID=1 " +
                $"and TimeCreated[timediff(@SystemTime) <= {(long)Lookback.TotalMilliseconds}]]]")
            { ReverseDirection = true };

            using var reader = new EventLogReader(query);
            using var latest = reader.ReadEvent();
            if (latest is null) return WakeCheck.AlreadyAwake;

            return latest.FormatDescription()?.Contains(TaskXml.TaskName(scheduleId), StringComparison.OrdinalIgnoreCase) == true
                ? WakeCheck.WokeOnSchedule
                : WakeCheck.OtherWakeSource;
        }
        catch (EventLogException e)
        {
            log.Write($"Wake check failed: {e.Message}");
            return WakeCheck.Unknown;
        }
    }
}
