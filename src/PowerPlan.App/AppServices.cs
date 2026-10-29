using PowerPlan.Core;
#if WINDOWS
using PowerPlan.Platform.Windows;
#endif

namespace PowerPlan.App;

/// <summary>Composition root: picks the platform layer and wires the shared services.</summary>
public sealed class AppServices
{
    public required AppSettings Settings { get; init; }
    public required ILog Log { get; init; }
    public required PowerCapabilities Capabilities { get; init; }
    public required IPowerController Power { get; init; }
    public required SleepRequestMarker SleepRequests { get; init; }
    public required ScheduleService Schedules { get; init; }
    public required ScheduledRunner Runner { get; init; }
    public required string DataDirectory { get; init; }

    public static AppServices Create()
    {
        var dir = AppPaths.DataDirectory;
        Directory.CreateDirectory(dir);
        var log = new FileLog(Path.Combine(dir, "powerplan.log"));
        var settings = AppSettings.Load(Path.Combine(dir, "settings.json"));

#if WINDOWS
        var capabilities = new WindowsCapabilityProbe().Probe();
        IPowerController realPower = new WindowsPowerController(capabilities, settings.ShutdownGraceSeconds, log);
        IScheduleBackend backend = new TaskSchedulerBackend(Environment.ProcessPath!, log);
        IWakeVerifier verifier = new WindowsWakeVerifier(log);
#else
        var capabilities = new PowerCapabilities(false, false, false, false, false, false);
        IPowerController realPower = new DryRunPowerController(log);
        IScheduleBackend backend = new UnsupportedBackend(log);
        IWakeVerifier verifier = new UnsupportedBackend(log);
#endif

        log.Write($"Started (dry-run: {settings.DryRun}) {capabilities}");
        var sleepRequests = new SleepRequestMarker(Path.Combine(dir, "sleep-request.txt"), TimeProvider.System);
        var power = new MarkingPowerController(settings.DryRun ? new DryRunPowerController(log) : realPower, sleepRequests);
        var schedules = new ScheduleService(new JsonScheduleStore(Path.Combine(dir, "schedules.json")), backend, TimeProvider.System, log);

        return new AppServices
        {
            Settings = settings,
            Log = log,
            Capabilities = capabilities,
            Power = power,
            SleepRequests = sleepRequests,
            Schedules = schedules,
            Runner = new ScheduledRunner(schedules, power, verifier, log),
            DataDirectory = dir,
        };
    }

#if !WINDOWS
    /// <summary>Placeholder until the macOS layer (pmset/launchd) exists.</summary>
    sealed class UnsupportedBackend(ILog log) : IScheduleBackend, IWakeVerifier
    {
        public void Register(Schedule schedule, DateTimeOffset runAt) => log.Write($"[unsupported OS] would arm '{schedule.Name}' for {runAt}");
        public void Unregister(Guid scheduleId) { }
        public WakeCheck Check(Guid scheduleId) => WakeCheck.Unknown;
    }
#endif
}
