using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using PowerPlan.App.ViewModels;
using PowerPlan.App.Views;
using PowerPlan.Core;
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
            // Only installed copies: a dev build would register its bin folder to start at sign-in.
            if (updater.IsInstalled) ApplyStartAtSignInDefault();
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

    static void ApplyStartAtSignInDefault()
    {
        if (Services.LoginItem is not { } loginItem) return;
        try
        {
            var settings = StartAtSignIn.ApplyDefault(Services.Settings, loginItem, Services.Log);
            if (settings != Services.Settings) Services.SaveSettings(settings);
        }
        catch (Exception e)
        {
            Services.Log.Write($"Turning on start at sign-in failed: {e.Message}");
        }
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

        // Most of the time NapPilot only lives in the tray, so a downloaded update is offered there too.
        updater.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(Updater.IsUpdateReady) || !updater.IsUpdateReady) return;
            var update = new NativeMenuItem($"Restart to update to {updater.ReadyVersion}");
            update.Click += (_, _) => updater.RestartNow();
            tray.Menu!.Items.Insert(0, update);
            tray.Menu.Items.Insert(1, new NativeMenuItemSeparator());
            tray.ToolTipText = $"NapPilot: version {updater.ReadyVersion} is ready to install";
        };
        tray.Clicked += (_, _) => ShowWindow();
        TrayIcon.SetIcons(this, [tray]);
    }

    void ShowWindow()
    {
        if (window is null)
        {
            window = new MainWindow();
            window.DataContext = new MainViewModel(Services, updater, new AvaloniaPasswordPrompt(window));
            window.Closed += (_, _) => window = null;
        }
        window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }
}
