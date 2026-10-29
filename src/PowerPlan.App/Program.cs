using Avalonia;
using Avalonia.Controls;

namespace PowerPlan.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var services = AppServices.Create();

        // Task Scheduler starts the app as "PowerPlan.App.exe --run <schedule id>": run it headless and exit.
        if (args is ["--run", var id])
        {
            try
            {
                return services.Runner.Run(Guid.Parse(id)) ? 0 : 1;
            }
            catch (Exception e)
            {
                services.Log.Write($"Run {id} failed: {e}");
                return 2;
            }
        }

        App.Services = services;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }

    // Also used by the visual designer.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
