using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Velopack;

namespace PowerPlan.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Must run first: the installer and updater start the exe with hook arguments and expect it to exit.
        var velopack = VelopackApp.Build();
        // macOS has no uninstaller: the app is just deleted, so there is no hook to run there.
        if (OperatingSystem.IsWindows())
            velopack.OnBeforeUninstallFastCallback(_ => AppServices.Create().RemoveFromSystem());
        velopack.Run();

        var services = AppServices.Create();

        // The OS scheduler starts the app as "NapPilot --run <schedule id> --at <unix seconds>": run it headless and exit.
        if (args is ["--run", var id, ..])
        {
            try
            {
                DateTimeOffset? due = args is [_, _, "--at", var at, ..] && long.TryParse(at, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                    ? DateTimeOffset.FromUnixTimeSeconds(seconds)
                    : null;
                return services.Runner.Run(Guid.Parse(id), due) ? 0 : 1;
            }
            catch (Exception e)
            {
                services.Log.Write($"Run {id} failed: {e}");
                return 2;
            }
        }

        using var instance = SingleInstance.TryClaim();
        if (instance is null)
        {
            services.Log.Write("Already running: asked the running app to show its window");
            return 0;
        }

        App.Services = services;
        App.Instance = instance;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }

    // Also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
