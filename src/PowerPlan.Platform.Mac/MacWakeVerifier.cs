using System.Globalization;
using System.Text.RegularExpressions;
using PowerPlan.Core;

namespace PowerPlan.Platform.Mac;

/// <summary>
/// Reads the latest wake in <c>pmset -g log</c>. A scheduled pmset wake reports an RTC (real-time clock) reason, e.g.
/// "Wake from Deep Idle [CDNVA] : due to RTC/Alarm". pmset does not name the owner, so any RTC wake counts.
/// Unverified on hardware: the reason text differs between Intel and Apple silicon and between macOS versions.
/// </summary>
public sealed partial class MacWakeVerifier : IWakeVerifier
{
    static readonly TimeSpan Lookback = TimeSpan.FromMinutes(10);

    readonly ILog log;
    readonly ICommandRunner runner;
    readonly TimeProvider time;

    public MacWakeVerifier(ILog log) : this(log, ProcessCommandRunner.Instance, TimeProvider.System)
    {
    }

    internal MacWakeVerifier(ILog log, ICommandRunner runner, TimeProvider time)
    {
        this.log = log;
        this.runner = runner;
        this.time = time;
    }

    public WakeCheck Check(Guid scheduleId)
    {
        var result = runner.Run(Tools.Pmset, "-g", "log");
        if (!result.Succeeded)
        {
            log.Write($"Wake check failed: pmset -g log returned {result.ExitCode}: {result.Error.Trim()}");
            return WakeCheck.Unknown;
        }
        return Classify(result.Output, time.GetUtcNow());
    }

    internal static WakeCheck Classify(string pmsetLog, DateTimeOffset now)
    {
        (DateTimeOffset At, string Reason)? latest = null;
        foreach (var line in pmsetLog.Split('\n'))
        {
            var match = WakeLine().Match(line.TrimEnd());
            if (!match.Success) continue;
            var at = DateTimeOffset.ParseExact(match.Groups["at"].Value, "yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
            if (latest is null || at >= latest.Value.At) latest = (at, match.Groups["reason"].Value);
        }

        if (latest is not { } wake || now - wake.At > Lookback) return WakeCheck.AlreadyAwake;
        return wake.Reason.Contains("RTC", StringComparison.OrdinalIgnoreCase) ? WakeCheck.WokeOnSchedule : WakeCheck.OtherWakeSource;
    }

    // "2026-10-30 06:59:02 +0300 Wake      \tWake from Deep Idle [CDNVA] : due to RTC/Alarm ...", DarkWake too.
    // "Wake Requests" lines list upcoming wakes, not wakes that happened.
    [GeneratedRegex(@"^(?<at>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2} [+-]\d{4})\s+(Dark)?Wake(?! Requests)\s+(?<reason>.*)$")]
    private static partial Regex WakeLine();
}
