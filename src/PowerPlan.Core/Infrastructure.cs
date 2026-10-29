using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerPlan.Core;

public static class AppPaths
{
    public const string ProductName = "NapPilot";

    /// <summary>NAPPILOT_DATA_DIR overrides it, for demos and manual tests that must not touch the real schedules.</summary>
    public static string DataDirectory { get; } =
        Environment.GetEnvironmentVariable("NAPPILOT_DATA_DIR") is { Length: > 0 } custom ? custom : Path.Combine(DataRoot, ProductName);

    // .NET maps ApplicationData to ~/.config on macOS; Mac apps keep their data in Application Support.
    static string DataRoot => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support")
        : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    /// <summary>Creates the data directory, moving it over from the app's old name (PowerPlan) on first start.</summary>
    public static string EnsureDataDirectory()
    {
        var old = Path.Combine(DataRoot, "PowerPlan");
        if (DataDirectory == Path.Combine(DataRoot, ProductName) && !Directory.Exists(DataDirectory) && Directory.Exists(old))
            Directory.Move(old, DataDirectory);
        Directory.CreateDirectory(DataDirectory);
        return DataDirectory;
    }
}

internal static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static List<T> LoadList<T>(string path) =>
        File.Exists(path) ? JsonSerializer.Deserialize<List<T>>(File.ReadAllText(path), Options) ?? [] : [];

    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Write then swap so the --run process never reads a half-written file.
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        File.Move(temp, path, overwrite: true);
    }
}

public sealed class FileLog(string path) : ILog
{
    readonly Lock gate = new();

    public void Write(string message)
    {
        lock (gate)
            File.AppendAllText(path, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}  {message}{Environment.NewLine}");
    }
}

public sealed class JsonScheduleStore(string path) : IScheduleStore
{
    public IReadOnlyList<Schedule> Load() => Json.LoadList<Schedule>(path);

    public void Save(IReadOnlyList<Schedule> schedules) => Json.Save(path, schedules);
}

public sealed class JsonHistoryStore(string path) : IHistoryStore
{
    public IReadOnlyList<RunRecord> Load() => Json.LoadList<RunRecord>(path);

    public void Save(IReadOnlyList<RunRecord> records) => Json.Save(path, records);
}

public sealed record AppSettings
{
    /// <summary>
    /// Log power actions instead of performing them. On by default in Debug builds so development never powers the
    /// PC off; off in Release builds, which are what users install.
    /// </summary>
    public bool DryRun { get; init; } = DefaultDryRun;

    /// <summary>Seconds of warning before a scheduled shutdown or restart; the user can cancel in that time.</summary>
    public int ShutdownGraceSeconds { get; init; } = 60;

    public bool CheckForUpdates { get; init; } = true;

#if DEBUG
    const bool DefaultDryRun = true;
#else
    const bool DefaultDryRun = false;
#endif

    public static AppSettings Load(string path)
    {
        if (File.Exists(path))
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json.Options) ?? new();

        var defaults = new AppSettings();
        defaults.Save(path);
        return defaults;
    }

    public void Save(string path) => Json.Save(path, this);
}

public sealed class DryRunPowerController(ILog log) : IPowerController
{
    public void Shutdown() => log.Write("[dry-run] would shut down");
    public void Restart() => log.Write("[dry-run] would restart");
    public void Sleep() => log.Write("[dry-run] would sleep");
    public void Hibernate() => log.Write("[dry-run] would hibernate");
    public void TurnDisplayOn() => log.Write("[dry-run] would turn the display on");
}

/// <summary>Picks the real or the dry-run controller on every call, so the dry-run setting applies without a restart.</summary>
public sealed class SwitchingPowerController(Func<bool> dryRun, IPowerController real, IPowerController dry) : IPowerController
{
    IPowerController Current => dryRun() ? dry : real;

    public void Shutdown() => Current.Shutdown();
    public void Restart() => Current.Restart();
    public void Sleep() => Current.Sleep();
    public void Hibernate() => Current.Hibernate();
    public void TurnDisplayOn() => Current.TurnDisplayOn();
}
