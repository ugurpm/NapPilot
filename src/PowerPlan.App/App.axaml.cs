using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using PowerPlan.App.ViewModels;
using PowerPlan.App.Views;
#if WINDOWS
using PowerPlan.Platform.Windows;
#endif

namespace PowerPlan.App;

public partial class App : Application
{
    static readonly TimeSpan UpdateInterval = TimeSpan.FromHours(6);

    public static AppServices Services { get; set; } = null!;
    public static SingleInstance Instance { get; set; } = null!;

    MainWindow? window;
#if WINDOWS
    IDisposable? sleepCatcher;
#endif
    Updater updater = null!;

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
            updater = new Updater(Services.Log);
            CreateTrayIcon(desktop);
            Instance.ShowRequested += () => Dispatcher.UIThread.Post(ShowWindow);
            desktop.Exit += (_, _) =>
            {
#if WINDOWS
                sleepCatcher?.Dispose();
#endif
                updater.ApplyOnExit();
            };

            if (desktop.Args?.Contains("--tray") != true) ShowWindow();
            StartUpdateChecks();
        }
        base.OnFrameworkInitializationCompleted();
    }

    void StartUpdateChecks()
    {
        async void Check()
        {
            if (Services.Settings.CheckForUpdates) await updater.CheckAsync();
        }
        Check();
        new DispatcherTimer(UpdateInterval, DispatcherPriority.Background, (_, _) => Check()).Start();
    }

    void CreateTrayIcon(IClassicDesktopStyleApplicationLifetime desktop)
    {
        var open = new NativeMenuItem("Open NapPilot");
        open.Click += (_, _) => ShowWindow();
        var sleep = new NativeMenuItem("Sleep now");
        sleep.Click += (_, _) => Services.Power.Sleep();
        var quit = new NativeMenuItem("Quit NapPilot");
        quit.Click += (_, _) => desktop.Shutdown();

        var tray = new TrayIcon
        {
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://NapPilot/Assets/nappilot.png"))),
            ToolTipText = "NapPilot",
            Menu = new NativeMenu { Items = { open, sleep, new NativeMenuItemSeparator(), quit } },
        };
        tray.Clicked += (_, _) => ShowWindow();
        TrayIcon.SetIcons(this, [tray]);
    }

    void ShowWindow()
    {
        if (window is null)
        {
            window = new MainWindow { DataContext = new MainViewModel(Services, updater) };
            window.Closed += (_, _) => window = null;
        }
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }
}
