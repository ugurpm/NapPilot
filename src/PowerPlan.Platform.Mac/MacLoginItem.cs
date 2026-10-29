using PowerPlan.Core;

namespace PowerPlan.Platform.Mac;

/// <summary>
/// A LaunchAgent with RunAtLoad starts the tray app at login. Inside a bundle <paramref name="exePath"/> is
/// <c>.../NapPilot.app/Contents/MacOS/NapPilot</c>. macOS 13+ lists it under Login Items → Allow in the Background.
/// </summary>
public sealed class MacLoginItem(string exePath, string agentsDirectory) : ILoginItem
{
    public const string Label = "com.nappilot.app";

    public MacLoginItem(string exePath) : this(exePath, Plist.LaunchAgentsDirectory)
    {
    }

    string PlistPath => Path.Combine(agentsDirectory, $"{Label}.plist");

    public bool IsEnabled => File.Exists(PlistPath);

    // Only the file changes: launchd loads it at the next login. Bootstrapping now would start a second tray app,
    // and booting out would quit the running one.
    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            File.Delete(PlistPath);
            return;
        }

        Directory.CreateDirectory(agentsDirectory);
        File.WriteAllText(PlistPath, Plist.ToText(Plist.Build(new Dictionary<string, object>
        {
            ["Label"] = Label,
            ["ProgramArguments"] = new[] { exePath, "--tray" },
            ["RunAtLoad"] = true,
            // Interactive: launchd does not throttle it like a background daemon.
            ["ProcessType"] = "Interactive",
        })));
    }
}
