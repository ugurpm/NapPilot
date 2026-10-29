using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerPlan.Core;

public static class AppPaths
{
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PowerPlan");
}

internal static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };
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
    public IReadOnlyList<Schedule> Load() =>
        File.Exists(path)
            ? JsonSerializer.Deserialize<List<Schedule>>(File.ReadAllText(path), Json.Options) ?? []
            : [];

    public void Save(IReadOnlyList<Schedule> schedules)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(schedules, Json.Options));
    }
}

public sealed record AppSettings
{
    /// <summary>Log power actions instead of performing them. On by default so development never powers the PC off.</summary>
    public bool DryRun { get; init; } = true;

    /// <summary>Seconds of warning before a scheduled shutdown or restart; the user can cancel in that time.</summary>
    public int ShutdownGraceSeconds { get; init; } = 60;

    public static AppSettings Load(string path)
    {
        if (File.Exists(path))
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json.Options) ?? new();

        var defaults = new AppSettings();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(defaults, Json.Options));
        return defaults;
    }
}

public sealed class DryRunPowerController(ILog log) : IPowerController
{
    public void Shutdown() => log.Write("[dry-run] would shut down");
    public void Restart() => log.Write("[dry-run] would restart");
    public void Sleep() => log.Write("[dry-run] would sleep");
    public void Hibernate() => log.Write("[dry-run] would hibernate");
    public void TurnDisplayOn() => log.Write("[dry-run] would turn the display on");
}
