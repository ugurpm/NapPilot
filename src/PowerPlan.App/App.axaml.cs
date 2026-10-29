using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using PowerPlan.App.ViewModels;
using PowerPlan.App.Views;
#if WINDOWS
using PowerPlan.Platform.Windows;
#endif

namespace PowerPlan.App;

public partial class App : Application
{
    public static AppServices Services { get; set; } = null!;

    MainWindow? window;
    IDisposable? sleepCatcher;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Services.Schedules.ArmAll();
#if WINDOWS
            var catcher = new SleepCatcher(
                () => Services.Schedules.HasPendingWake,
                Services.SleepRequests.IsRecent,
                Services.Power.Hibernate,
                TimeProvider.System,
                Services.Log);
            catcher.Start();
            sleepCatcher = catcher;
#endif
            CreateTrayIcon(desktop);
            desktop.Exit += (_, _) => sleepCatcher?.Dispose();

            if (desktop.Args?.Contains("--tray") != true) ShowWindow();
        }
        base.OnFrameworkInitializationCompleted();
    }

    void CreateTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var open = new NativeMenuItem("Open PowerPlan");
        open.Click += (_, _) => ShowWindow();
        var sleep = new NativeMenuItem("Sleep now");
        sleep.Click += (_, _) => Services.Power.Sleep();
        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => desktop.Shutdown();

        var tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://PowerPlan.App/Assets/powerplan.png"))),
            ToolTipText = "PowerPlan",
            Menu = new NativeMenu { Items = { open, sleep, new NativeMenuItemSeparator(), quit } },
        };
        tray.Clicked += (_, _) => ShowWindow();
        TrayIcon.SetIcons(this, [tray]);
    }

    void ShowWindow()
    {
        if (window is null)
        {
            window = new MainWindow { DataContext = new MainViewModel(Services) };
            window.Closed += (_, _) => window = null;
        }
        window.Show();
        window.Activate();
    }
}
