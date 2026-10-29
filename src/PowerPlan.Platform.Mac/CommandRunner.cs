using System.Diagnostics;

namespace PowerPlan.Platform.Mac;

internal readonly record struct CommandResult(int ExitCode, string Output, string Error)
{
    public bool Succeeded => ExitCode == 0;
    public string Text => (Output + Error).Trim();
}

/// <summary>Runs the macOS command-line tools; tests replace it so nothing real runs.</summary>
internal interface ICommandRunner
{
    CommandResult Run(string file, params string[] args);
}

internal sealed class ProcessCommandRunner : ICommandRunner
{
    public static readonly ProcessCommandRunner Instance = new();

    public CommandResult Run(string file, params string[] args)
    {
        var start = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);

        using var process = Process.Start(start)!;
        // Read stderr concurrently: a tool that fills one pipe while we block on the other would hang.
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return new(process.ExitCode, output, error.Result);
    }
}

/// <summary>Absolute paths: a LaunchAgent starts with a minimal PATH.</summary>
internal static class Tools
{
    public const string Launchctl = "/bin/launchctl";
    public const string Pmset = "/usr/bin/pmset";
    public const string Osascript = "/usr/bin/osascript";
    public const string Caffeinate = "/usr/bin/caffeinate";
    public const string Id = "/usr/bin/id";
}

internal static class AppleScript
{
    /// <summary>The AppleScript error number when the user presses Cancel in a dialog or password prompt.</summary>
    const string UserCanceled = "(-128)";

    /// <summary>An AppleScript string literal: only backslash and double quote need escaping.</summary>
    public static string Quote(string text) => $"\"{text.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

    public static bool WasCanceled(CommandResult result) => !result.Succeeded && result.Text.Contains(UserCanceled);

    public static CommandResult Run(ICommandRunner runner, string script) => runner.Run(Tools.Osascript, "-e", script);
}
