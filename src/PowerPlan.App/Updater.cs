using System.Reflection;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using PowerPlan.Core;
using Velopack;
using Velopack.Sources;

namespace PowerPlan.App;

public static class AppInfo
{
    public const string RepoUrl = "https://github.com/ugurpm/PowerPlan";
    public const string WebsiteUrl = "https://ugurpm.github.io/PowerPlan/";

    /// <summary>The release version without the "+commit" suffix the SDK appends.</summary>
    public static string Version { get; } =
        (Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0")
        .Split('+')[0];
}

/// <summary>
/// Looks for a newer release on GitHub, downloads it in the background and installs it on restart (or on quit).
/// Only an installed copy can update itself; `dotnet run` builds just say so.
/// </summary>
public sealed partial class Updater(ILog log) : ObservableObject
{
    readonly UpdateManager manager = new(new GithubSource(AppInfo.RepoUrl, null, false));
    UpdateInfo? ready;
    bool checking;

    [ObservableProperty] string status = "";
    [ObservableProperty] bool isUpdateReady;
    [ObservableProperty] string readyVersion = "";

    public bool IsInstalled => manager.IsInstalled;

    public async Task CheckAsync()
    {
        if (!manager.IsInstalled)
        {
            Status = "Automatic updates work in the installed app (not in a development build).";
            return;
        }
        if (checking || ready is not null) return;
        checking = true;
        try
        {
            Status = "Checking for updates…";
            var info = await manager.CheckForUpdatesAsync();
            if (info is null)
            {
                Status = $"NapPilot is up to date (checked {DateTime.Now:HH:mm}).";
                return;
            }

            var version = info.TargetFullRelease.Version.ToString();
            log.Write($"Update {version} found, downloading");
            await manager.DownloadUpdatesAsync(info, percent =>
                Dispatcher.UIThread.Post(() => Status = $"Downloading version {version}… {percent}%"));

            ready = info;
            ReadyVersion = version;
            IsUpdateReady = true;
            Status = $"Version {version} is ready. Restart NapPilot to install it.";
        }
        catch (Exception e)
        {
            log.Write($"Update check failed: {e.Message}");
            Status = "Could not check for updates. NapPilot will try again later.";
        }
        finally
        {
            checking = false;
        }
    }

    public void RestartNow()
    {
        if (ready is null) return;
        log.Write($"Restarting to install {ReadyVersion}");
        manager.ApplyUpdatesAndRestart(ready.TargetFullRelease);
    }

    /// <summary>Installs a downloaded update after the app quits, without starting it again.</summary>
    public void ApplyOnExit()
    {
        if (ready is null) return;
        log.Write($"Installing {ReadyVersion} after exit");
        manager.WaitExitThenApplyUpdates(ready.TargetFullRelease, silent: true, restart: false);
    }
}
