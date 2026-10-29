using PowerPlan.Core;
#if WINDOWS
using PowerPlan.Platform.Windows;
#else
using PowerPlan.Platform.Mac;
#endif

namespace PowerPlan.App;

/// <summary>Composition root: picks the platform layer and wires the shared services.</summary>
public sealed class AppServices
{
    public required AppSettings Settings { get; set; }
    public required ILog Log { get; init; }
    public required PowerCapabilities Capabilities { get; init; }
    public required IPowerController Power { get; init; }
    public required SleepRequestMarker SleepRequests { get; init; }
    public required ScheduleService Schedules { get; init; }
    public required ScheduledRunner Runner { get; init; }
    /// <summary>Null on an OS without a platform layer.</summary>
    public required ILoginItem? LoginItem { get; init; }
    public required string DataDirectory { get; init; }

    string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    public void SaveSettings(AppSettings settings)
    {
        settings.Save(SettingsPath);
        Settings = settings;
        Log.Write($"Settings changed: {settings}");
    }

    /// <summary>Called by the uninstaller: nothing NapPilot registered with the OS may outlive the app.</summary>
    public void RemoveFromSystem()
    {
        Schedules.UnregisterAll();
        LoginItem?.SetEnabled(false);
        Log.Write("Uninstalling: removed scheduled tasks and the login item");
    }

    public static AppServices Create()
    {
        var dir = AppPaths.EnsureDataDirectory();
        var log = new FileLog(Path.Combine(dir, "nappilot.log"));
        var settings = AppSettings.Load(Path.Combine(dir, "settings.json"));
        var exe = Environment.ProcessPath!;

#if WINDOWS
        var capabilities = new WindowsCapabilityProbe().Probe();
        IPowerController realPower = new WindowsPowerController(capabilities, settings.ShutdownGraceSeconds, log);
        IScheduleBackend backend = new TaskSchedulerBackend(exe, log);
        IWakeVerifier verifier = new WindowsWakeVerifier(log);
        ILoginItem? loginItem = new WindowsLoginItem(exe);
#else
        PowerCapabilities capabilities;
        IPowerController realPower;
        IScheduleBackend backend;
        IWakeVerifier verifier;
        ILoginItem? loginItem;
        if (OperatingSystem.IsMacOS())
        {
            capabilities = new MacCapabilityProbe().Probe();
            realPower = new MacPowerController(settings.ShutdownGraceSeconds, log);
            backend = new MacScheduleBackend(exe, dir, log);
            verifier = new MacWakeVerifier(log);
            loginItem = new MacLoginItem(exe);
        }
        else
        {
            capabilities = new PowerCapabilities(false, false, false, false, false, false);
            realPower = new DryRunPowerController(log);
            backend = new UnsupportedBackend(log);
            verifier = (UnsupportedBackend)backend;
            loginItem = null;
        }
#endif

        log.Write($"Started {AppInfo.Version} (dry-run: {settings.DryRun}) {capabilities}");
        var sleepRequests = new SleepRequestMarker(Path.Combine(dir, "sleep-request.txt"), TimeProvider.System);
        AppServices? services = null;
        Func<bool> dryRun = () => services!.Settings.DryRun;
        var power = new MarkingPowerController(new SwitchingPowerController(dryRun, realPower, new DryRunPowerController(log)), sleepRequests);
        var schedules = new ScheduleService(
            new JsonScheduleStore(Path.Combine(dir, "schedules.json")),
            new JsonHistoryStore(Path.Combine(dir, "history.json")),
            backend, TimeProvider.System, log);

        return services = new AppServices
        {
            Settings = settings,
            Log = log,
            Capabilities = capabilities,
            Power = power,
            SleepRequests = sleepRequests,
            Schedules = schedules,
            Runner = new ScheduledRunner(schedules, power, verifier, dryRun, log),
            LoginItem = loginItem,
            DataDirectory = dir,
        };
    }

#if !WINDOWS
    /// <summary>Linux has no platform layer yet: scheduling is only logged.</summary>
    sealed class UnsupportedBackend(ILog log) : IScheduleBackend, IWakeVerifier
    {
        public void Register(Schedule schedule, DateTimeOffset runAt) => log.Write($"[unsupported OS] would arm '{schedule.Name}' for {runAt}");
        public void Unregister(Guid scheduleId) { }
        public WakeCheck Check(Guid scheduleId) => WakeCheck.Unknown;
    }
#endif
}
