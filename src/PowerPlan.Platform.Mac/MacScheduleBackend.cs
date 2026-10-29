using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using PowerPlan.Core;

namespace PowerPlan.Platform.Mac;

/// <summary>Builds the per-user LaunchAgent that runs one schedule once.</summary>
internal static class LaunchJob
{
    /// <summary>Set in the job's environment so the <c>--run</c> process knows which job it is.</summary>
    public const string EnvironmentVariable = "NAPPILOT_JOB";

    public static string Prefix(Guid scheduleId) => $"com.nappilot.run.{scheduleId:N}";

    // The run time is part of the label: re-arming from inside a running job loads a new job instead of
    // booting out the current one, which would stop the --run process before it acts.
    public static string Label(Guid scheduleId, DateTimeOffset runAt) =>
        $"{Prefix(scheduleId)}.{runAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}";

    public static XDocument Build(Schedule schedule, DateTimeOffset runAt, string exePath, TimeZoneInfo zone)
    {
        var label = Label(schedule.Id, runAt);
        // launchd has no year; the job is replaced after it runs, long before the date comes round again.
        var local = TimeZoneInfo.ConvertTime(runAt, zone);
        return Plist.Build(new Dictionary<string, object>
        {
            ["Label"] = label,
            ["ProgramArguments"] = new[]
            {
                exePath, "--run", schedule.Id.ToString(), "--at", runAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            },
            ["StartCalendarInterval"] = new Dictionary<string, object>
            {
                ["Month"] = local.Month,
                ["Day"] = local.Day,
                ["Hour"] = local.Hour,
                ["Minute"] = local.Minute,
            },
            ["EnvironmentVariables"] = new Dictionary<string, object> { [EnvironmentVariable] = label },
        });
    }
}

internal static class PmsetDate
{
    /// <summary>The <c>MM/dd/yy HH:mm:ss</c> wall-clock format from <c>man pmset</c>, independent of the user's locale.</summary>
    public static string Format(DateTimeOffset instant, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(instant, zone).ToString("MM/dd/yy HH:mm:ss", CultureInfo.InvariantCulture);
}

/// <summary>The hardware wakes NapPilot has given pmset, per schedule, so re-arming does not ask for a password again.</summary>
internal sealed class WakeLedger(string path)
{
    public Dictionary<Guid, List<DateTimeOffset>> Load() =>
        File.Exists(path)
            ? JsonSerializer.Deserialize<Dictionary<Guid, List<DateTimeOffset>>>(File.ReadAllText(path)) ?? []
            : [];

    public void Save(Dictionary<Guid, List<DateTimeOffset>> wakes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(wakes, new JsonSerializerOptions { WriteIndented = true }));
    }
}

/// <summary>
/// One LaunchAgent per schedule runs the app at the next occurrence. Wake schedules also get pmset hardware wakes,
/// which need root: they are scheduled 30 days ahead in one admin prompt so the daily re-arm does not ask again.
/// </summary>
public sealed class MacScheduleBackend : IScheduleBackend
{
    internal static readonly TimeSpan WakeLead = TimeSpan.FromMinutes(1);
    internal static readonly TimeSpan WakeHorizon = TimeSpan.FromDays(30);
    /// <summary>Outside a run job (the tray app at login, the window) wakes are topped up when fewer days than this remain.</summary>
    internal static readonly TimeSpan TopUpWhenLeft = TimeSpan.FromDays(7);
    const string PmsetOwner = "NapPilot";

    readonly string exePath;
    readonly ILog log;
    readonly ICommandRunner runner;
    readonly string agentsDirectory;
    readonly TimeProvider time;
    readonly string? currentJob;
    readonly WakeLedger ledger;
    readonly LaunchdDomain launchd;

    public MacScheduleBackend(string exePath, string dataDirectory, ILog log)
        : this(exePath, dataDirectory, log, ProcessCommandRunner.Instance, Plist.LaunchAgentsDirectory, TimeProvider.System,
            Environment.GetEnvironmentVariable(LaunchJob.EnvironmentVariable))
    {
    }

    internal MacScheduleBackend(string exePath, string dataDirectory, ILog log, ICommandRunner runner, string agentsDirectory,
        TimeProvider time, string? currentJob)
    {
        this.exePath = exePath;
        this.log = log;
        this.runner = runner;
        this.agentsDirectory = agentsDirectory;
        this.time = time;
        this.currentJob = string.IsNullOrEmpty(currentJob) ? null : currentJob;
        ledger = new WakeLedger(Path.Combine(dataDirectory, "mac-wakes.json"));
        launchd = new LaunchdDomain(runner);
    }

    public void Register(Schedule schedule, DateTimeOffset runAt)
    {
        var zone = time.LocalTimeZone;
        // Wakes first: if the user cancels the password prompt nothing else changes and Core does not save.
        SyncWakes(schedule.Id, schedule.Action == PowerAction.Wake ? WakeTimes(schedule, runAt, zone) : [], "scheduled");

        var label = LaunchJob.Label(schedule.Id, runAt);
        var path = PlistPath(label);
        var text = Plist.ToText(LaunchJob.Build(schedule, runAt, exePath, zone));

        var loaded = launchd.Print(label).Succeeded;
        // Already armed (e.g. re-armed at startup): reloading would stop the job if it is running right now.
        if (!loaded || !File.Exists(path) || File.ReadAllText(path) != text)
        {
            if (loaded) launchd.Bootout(label);
            Directory.CreateDirectory(agentsDirectory);
            File.WriteAllText(path, text);
            var result = launchd.Bootstrap(path);
            if (!result.Succeeded)
            {
                File.Delete(path);
                throw new InvalidOperationException($"launchctl bootstrap {label} failed ({result.ExitCode}): {result.Text}");
            }
        }
        RemoveJobs(schedule.Id, keep: label);
    }

    public void Unregister(Guid scheduleId)
    {
        SyncWakes(scheduleId, [], "removed");
        RemoveJobs(scheduleId, keep: null);
    }

    /// <summary>Wake times (a minute early, so the Mac is up when the job starts) for occurrences in the next 30 days.</summary>
    internal List<DateTimeOffset> WakeTimes(Schedule schedule, DateTimeOffset runAt, TimeZoneInfo zone)
    {
        var now = time.GetUtcNow();
        var wakes = new List<DateTimeOffset>();
        for (DateTimeOffset? next = runAt; next is { } at && at <= runAt + WakeHorizon; next = ScheduleCalculator.NextOccurrence(schedule, at, zone))
        {
            if (at - WakeLead > now) wakes.Add(at - WakeLead);
        }
        return wakes;
    }

    void SyncWakes(Guid scheduleId, List<DateTimeOffset> desired, string verb)
    {
        var now = time.GetUtcNow();
        var all = ledger.Load();
        var recorded = (all.GetValueOrDefault(scheduleId) ?? []).Where(w => w > now).ToHashSet();
        var coveredUntil = recorded.Count > 0 ? recorded.Max() : DateTimeOffset.MinValue;

        var stale = recorded.Where(w => !desired.Contains(w)).ToList();
        var missing = desired.Where(w => !recorded.Contains(w)).ToList();
        var covered = stale.Count == 0 && missing.All(w => w > coveredUntil) && (desired.Count == 0 || recorded.Contains(desired[0]));
        var runningLow = missing.Count > 0 && coveredUntil - now < TopUpWhenLeft;

        // Inside a run job nobody may be at the Mac, so a still-covered next run never asks for a password.
        if (!covered || (runningLow && currentJob is null))
        {
            // Two schedules can share a wake time; pmset keeps one entry, so leave it to the other schedule.
            var others = all.Where(e => e.Key != scheduleId).SelectMany(e => e.Value).ToHashSet();
            var cancel = stale.Where(w => !others.Contains(w)).ToList();
            var add = missing.Where(w => !others.Contains(w)).ToList();
            if (cancel.Count + add.Count > 0) RunPmsetAsAdmin(cancel, add, verb);
            recorded = desired.ToHashSet();
        }

        var before = all.GetValueOrDefault(scheduleId) ?? [];
        if (recorded.Count == 0 && before.Count == 0) return;
        if (recorded.Count == 0) all.Remove(scheduleId);
        else all[scheduleId] = recorded.Order().ToList();
        if (!before.SequenceEqual(all.GetValueOrDefault(scheduleId) ?? [])) ledger.Save(all);
    }

    void RunPmsetAsAdmin(List<DateTimeOffset> cancel, List<DateTimeOffset> add, string verb)
    {
        var zone = time.LocalTimeZone;
        // A wake that already passed or was removed by hand makes cancel fail; that must not stop the rest.
        var commands = cancel.Select(w => $"{Tools.Pmset} schedule cancel wake '{PmsetDate.Format(w, zone)}' {PmsetOwner} || true")
            .Concat(add.Select(w => $"{Tools.Pmset} schedule wake '{PmsetDate.Format(w, zone)}' {PmsetOwner}"));
        var script = $"do shell script {AppleScript.Quote(string.Join(" && ", commands))} " +
                     $"with prompt {AppleScript.Quote("NapPilot needs an administrator password to change when your Mac wakes.")} " +
                     "with administrator privileges";

        log.Write($"pmset: cancelling {cancel.Count} and adding {add.Count} wakes (admin prompt)");
        var result = AppleScript.Run(runner, script);
        if (AppleScript.WasCanceled(result))
            throw new InvalidOperationException($"The administrator password prompt was cancelled, so the wake was not {verb}.");
        if (!result.Succeeded)
            throw new InvalidOperationException($"pmset could not change the scheduled wakes ({result.ExitCode}): {result.Text}");
    }

    /// <summary>Boots out and deletes this schedule's other jobs, except one that is running (it may be us).</summary>
    void RemoveJobs(Guid scheduleId, string? keep)
    {
        if (!Directory.Exists(agentsDirectory)) return;
        foreach (var file in Directory.EnumerateFiles(agentsDirectory, $"{LaunchJob.Prefix(scheduleId)}*.plist"))
        {
            var label = Path.GetFileNameWithoutExtension(file);
            if (label == keep) continue;
            if (label == currentJob || launchd.Print(label).Text.Contains("state = running"))
            {
                // Its one-off date has passed, so it cannot fire again; the next re-arm removes it.
                log.Write($"Leaving running job {label} for the next re-arm");
                continue;
            }
            launchd.Bootout(label);
            File.Delete(file);
        }
    }

    string PlistPath(string label) => Path.Combine(agentsDirectory, $"{label}.plist");
}
