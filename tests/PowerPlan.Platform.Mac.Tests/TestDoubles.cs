using PowerPlan.Core;
using PowerPlan.Platform.Mac;

namespace PowerPlan.Platform.Mac.Tests;

sealed class FixedTime(DateTimeOffset now, TimeZoneInfo zone) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();
    public override TimeZoneInfo LocalTimeZone => zone;
}

sealed class ListLog : ILog
{
    public List<string> Lines { get; } = [];
    public void Write(string message) => Lines.Add(message);
}

/// <summary>Records every command and plays launchctl's part, so no real tool ever runs.</summary>
sealed class FakeRunner : ICommandRunner
{
    public List<string[]> Calls { get; } = [];
    public HashSet<string> Loaded { get; } = [];
    public HashSet<string> Running { get; } = [];
    public Func<string, string[], CommandResult?> Respond { get; set; } = (_, _) => null;

    public IEnumerable<string> Scripts => Calls.Where(c => c[0] == Tools.Osascript).Select(c => c[2]);

    public IEnumerable<string[]> Launchctl(string verb) => Calls.Where(c => c[0] == Tools.Launchctl && c[1] == verb);

    public CommandResult Run(string file, params string[] args)
    {
        Calls.Add([file, .. args]);
        if (Respond(file, args) is { } response) return response;
        return file switch
        {
            Tools.Id => new(0, "501\n", ""),
            Tools.Launchctl => FakeLaunchctl(args),
            _ => new(0, "", ""),
        };
    }

    CommandResult FakeLaunchctl(string[] args)
    {
        switch (args[0])
        {
            case "bootstrap":
                Assert.Equal("gui/501", args[1]);
                Loaded.Add(Path.GetFileNameWithoutExtension(args[2]));
                return new(0, "", "");
            case "bootout":
                return Loaded.Remove(Label(args[1])) ? new(0, "", "") : new(3, "", "Boot-out failed: 3: No such process");
            case "print":
                var label = Label(args[1]);
                if (!Loaded.Contains(label)) return new(113, "", "Could not find service");
                return new(0, $"{args[1]} = {{\n\tstate = {(Running.Contains(label) ? "running" : "not running")}\n}}", "");
            default:
                throw new InvalidOperationException($"unexpected launchctl {args[0]}");
        }
    }

    static string Label(string target)
    {
        Assert.StartsWith("gui/501/", target);
        return target["gui/501/".Length..];
    }
}

sealed class TempDir : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("nappilot-mac-tests-").FullName;
    public void Dispose() => Directory.Delete(Path, recursive: true);
}

static class Zones
{
    // +03:00 all year, so local and UTC dates differ in the evening.
    public static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
}
