using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerPlan.Core;

namespace PowerPlan.App.ViewModels;

public sealed partial class DayOption(DayOfWeek day) : ObservableObject
{
    public DayOfWeek Day => day;
    public string Label { get; } = CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(day);
    [ObservableProperty] bool isSelected;
}

public sealed record ActionFilter(string Label, PowerAction? Action);

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    public const string FilterAll = "All", FilterUpcoming = "Upcoming", FilterPast = "Past";

    readonly AppServices services;
    readonly IPasswordPrompt passwordPrompt;
    readonly DispatcherTimer clock;
    bool loadingSettings;

    public MainViewModel(AppServices services, Updater updater, IPasswordPrompt passwordPrompt)
    {
        this.services = services;
        this.passwordPrompt = passwordPrompt;
        Updater = updater;

        var wake = WakeAdvisor.Assess(services.Capabilities);
        WakeNotes = wake.CanWake ? wake.Notes : ["Scheduled wake is not available on this computer.", .. wake.Notes];

        // Monday first, as on most calendars outside the US.
        Days = [.. Enumerable.Range(1, 7).Select(i => new DayOption((DayOfWeek)(i % 7)))];
        foreach (var day in Days) day.PropertyChanged += (_, _) => UpdatePreview();
        ResetEditor();
        LoadSettings();

        // Relative times ("in 5 min") and runs made by the scheduler while the window is open.
        clock = new DispatcherTimer(TimeSpan.FromSeconds(20), DispatcherPriority.Background, (_, _) => Refresh());
        clock.Start();
        Refresh();
    }

    public Updater Updater { get; }
    public string Version => $"Version {AppInfo.Version}";
    public string DataDirectory => services.DataDirectory;
    public IReadOnlyList<string> WakeNotes { get; }
    public bool HasWakeNotes => WakeNotes.Count > 0;

    // ---------- Editor ----------

    public IReadOnlyList<ActionStyle> Actions => ActionStyle.All;
    public IReadOnlyList<DayOption> Days { get; }

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsEditing), nameof(EditorTitle), nameof(SaveLabel))]
    Guid? editingId;

    [ObservableProperty] string name = "";
    [ObservableProperty] ActionStyle selectedAction = ActionStyle.All[0];
    [ObservableProperty] TimeSpan? time;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsRepeat))] bool isOnce;
    [ObservableProperty] DateTime? date;
    [ObservableProperty] bool isActive = true;
    [ObservableProperty] string preview = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError))] string? error;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasNotice))] string? notice;

    public bool IsRepeat
    {
        get => !IsOnce;
        set => IsOnce = !value;
    }

    public bool IsEditing => EditingId is not null;
    public string EditorTitle => IsEditing ? "Edit schedule" : "New schedule";
    public string SaveLabel => IsEditing ? "Save changes" : "Add schedule";
    public bool HasError => Error is not null;
    public bool HasNotice => Notice is not null;

    partial void OnSelectedActionChanged(ActionStyle value) => UpdatePreview();
    partial void OnTimeChanged(TimeSpan? value) => UpdatePreview();
    partial void OnIsOnceChanged(bool value) => UpdatePreview();
    partial void OnDateChanged(DateTime? value) => UpdatePreview();
    partial void OnIsActiveChanged(bool value) => UpdatePreview();

    /// <summary>Quick picks: a one-off this many minutes from now.</summary>
    [RelayCommand]
    void In(string minutes)
    {
        var at = DateTime.Now.AddMinutes(int.Parse(minutes, CultureInfo.InvariantCulture));
        IsOnce = true;
        Date = at.Date;
        Time = new TimeSpan(at.Hour, at.Minute, 0);
    }

    [RelayCommand]
    void At(string time) => Time = TimeSpan.Parse(time, CultureInfo.InvariantCulture);

    [RelayCommand]
    void PickDays(string preset)
    {
        var chosen = preset switch
        {
            "weekdays" => Recurrence.Weekdays.Days,
            "weekends" => Recurrence.Weekends.Days,
            "none" => [],
            _ => Recurrence.Daily.Days,
        };
        foreach (var day in Days) day.IsSelected = chosen.Contains(day.Day);
    }

    [RelayCommand]
    async Task Save()
    {
        Notice = null;
        if (Time is not { } time)
        {
            Error = "Pick a time.";
            return;
        }
        if (IsOnce && Date is null)
        {
            Error = "Pick a date.";
            return;
        }
        if (!await EnsureUnlockedAsync()) return;

        var schedule = new Schedule
        {
            Id = EditingId ?? Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(Name) ? SelectedAction.Label : Name.Trim(),
            Action = SelectedAction.Action,
            Time = new TimeOnly(time.Hours, time.Minutes),
            Recurrence = IsOnce
                ? Recurrence.Once(DateOnly.FromDateTime(Date!.Value))
                : Recurrence.Weekly([.. Days.Where(d => d.IsSelected).Select(d => d.Day)]),
            Enabled = IsActive,
        };

        try
        {
            var errors = services.Schedules.Save(schedule);
            if (errors.Count > 0)
            {
                Error = string.Join(" ", errors);
                return;
            }
            Notice = IsEditing ? $"Saved changes to '{schedule.Name}'." : $"Added '{schedule.Name}'.";
            ResetEditor();
        }
        catch (Exception e)
        {
            services.Log.Write($"Saving schedule failed: {e}");
            Error = $"Could not register the schedule with the system: {e.Message}";
        }
        Refresh();
    }

    [RelayCommand]
    void CancelEdit()
    {
        ResetEditor();
        Notice = null;
    }

    void ResetEditor()
    {
        EditingId = null;
        Name = "";
        SelectedAction = ActionStyle.All[0];
        var soon = DateTime.Now.AddMinutes(30);
        Time = new TimeSpan(soon.Hour, soon.Minute, 0);
        IsOnce = false;
        Date = DateTime.Today;
        IsActive = true;
        PickDays("weekdays");
        Error = null;
        UpdatePreview();
    }

    void UpdatePreview()
    {
        if (Time is not { } time || (IsOnce && Date is null))
        {
            Preview = "";
            return;
        }
        var draft = new Schedule
        {
            Name = "preview",
            Time = new TimeOnly(time.Hours, time.Minutes),
            Recurrence = IsOnce
                ? Recurrence.Once(DateOnly.FromDateTime(Date!.Value))
                : Recurrence.Weekly([.. Days.Where(d => d.IsSelected).Select(d => d.Day)]),
        };
        var next = services.Schedules.NextRun(draft);
        Preview = !IsActive ? "Paused: it will not run until you resume it."
            : next is { } n ? $"{SelectedAction.Label} on {n.ToLocalTime():dddd d MMMM 'at' HH:mm} · {EntryRow.Relative(n - services.Schedules.Now)}"
            : IsOnce ? "That time has already passed."
            : "Pick at least one day.";
    }

    // ---------- List ----------

    public ObservableCollection<EntryRow> Rows { get; } = [];
    public IReadOnlyList<string> Filters { get; } = [FilterAll, FilterUpcoming, FilterPast];
    public IReadOnlyList<ActionFilter> ActionFilters { get; } =
        [new("All actions", null), .. ActionStyle.All.Select(s => new ActionFilter(s.Label, s.Action))];

    [ObservableProperty] string selectedFilter = FilterAll;
    [ObservableProperty] ActionFilter selectedActionFilter = new("All actions", null);
    [ObservableProperty] string searchText = "";
    [ObservableProperty] int upcomingCount;
    [ObservableProperty] int pastCount;
    [ObservableProperty] string emptyText = "";
    [ObservableProperty] bool isEmpty;

    partial void OnSelectedFilterChanged(string value) => Refresh();
    partial void OnSelectedActionFilterChanged(ActionFilter value) => Refresh();
    partial void OnSearchTextChanged(string value) => Refresh();

    [RelayCommand]
    void Edit(EntryRow row)
    {
        if (row.Entry.Schedule is not { } s) return;
        EditingId = s.Id;
        Name = s.Name;
        SelectedAction = ActionStyle.Of(s.Action);
        Time = s.Time.ToTimeSpan();
        IsOnce = s.Recurrence.IsOnce;
        Date = s.Recurrence.Date?.ToDateTime(TimeOnly.MinValue) ?? DateTime.Today;
        foreach (var day in Days) day.IsSelected = s.Recurrence.Days.Contains(day.Day);
        IsActive = s.Enabled || row.IsPast;
        Error = null;
        Notice = null;
        UpdatePreview();
    }

    [RelayCommand]
    async Task Delete(EntryRow row)
    {
        if (!await EnsureUnlockedAsync()) return;
        try
        {
            if (row.IsPast) services.Schedules.RemovePast(row.Entry);
            else if (row.Entry.Schedule is { } s) services.Schedules.Remove(s.Id);
            if (row.Entry.Schedule?.Id is { } id && id == EditingId) ResetEditor();
            Notice = $"Deleted '{row.Name}'.";
        }
        catch (Exception e)
        {
            services.Log.Write($"Delete failed: {e}");
            Error = $"Could not delete: {e.Message}";
        }
        Refresh();
    }

    [RelayCommand]
    async Task Toggle(EntryRow row)
    {
        if (row.Entry.Schedule is not { } s) return;
        if (!await EnsureUnlockedAsync()) return;
        try
        {
            var errors = services.Schedules.SetEnabled(s.Id, row.IsPaused);
            Error = errors.Count > 0 ? string.Join(" ", errors) : null;
        }
        catch (Exception e)
        {
            services.Log.Write($"Pause/resume failed: {e}");
            Error = $"Could not update the schedule: {e.Message}";
        }
        Refresh();
    }

    [RelayCommand]
    async Task ClearPast()
    {
        if (!await EnsureUnlockedAsync()) return;
        services.Schedules.ClearPast();
        Notice = "Cleared past schedules.";
        Refresh();
    }

    void Refresh()
    {
        var now = services.Schedules.Now;
        var all = services.Schedules.Overview().Select(e => new EntryRow(e, now)).ToList();
        UpcomingCount = all.Count(r => !r.IsPast);
        PastCount = all.Count(r => r.IsPast);

        Rows.Clear();
        foreach (var row in all.Where(r => r.Matches(SelectedFilter, SelectedActionFilter?.Action, SearchText.Trim())))
            Rows.Add(row);

        IsEmpty = Rows.Count == 0;
        EmptyText = all.Count == 0
            ? "No schedules yet. Add one on the left: pick an action, a time and when it repeats."
            : "Nothing matches these filters.";
        UpdatePreview();
    }

    // ---------- Parental mode ----------

    public bool ParentalModeEnabled => services.Settings.ParentalModeEnabled;

    [ObservableProperty, NotifyPropertyChangedFor(nameof(LockLabel), nameof(IsLocked), nameof(HeaderLockText))]
    bool isUnlocked;

    /// <summary>True only when parental mode is on and this window hasn't unlocked it yet.</summary>
    public bool IsLocked => ParentalModeEnabled && !IsUnlocked;

    public string LockLabel => !ParentalModeEnabled ? "Off" : IsUnlocked ? "Unlocked" : "Locked";

    /// <summary>Shown in the header at all times, so whether schedules are password-protected is never a guess.</summary>
    public string HeaderLockText => !ParentalModeEnabled ? "Parental mode: Off"
        : IsUnlocked ? "\U0001F513 Parental mode: Unlocked" : "\U0001F512 Parental mode: Locked";

    /// <summary>Call before any schedule mutation. Prompts for the password the first time; stays unlocked until the window closes or Lock is pressed.</summary>
    async Task<bool> EnsureUnlockedAsync()
    {
        if (!ParentalModeEnabled || IsUnlocked) return true;

        var password = await passwordPrompt.ConfirmAsync(
            "Parental mode", "Enter the parental mode password to change schedules.", pwd => ParentalMode.Verify(services.Settings, pwd));
        if (password is null) return false;

        IsUnlocked = true;
        return true;
    }

    [RelayCommand]
    async Task ToggleLock()
    {
        if (IsUnlocked) IsUnlocked = false;
        else await EnsureUnlockedAsync();
    }

    [RelayCommand]
    async Task TurnOnParentalMode()
    {
        var password = await passwordPrompt.SetAsync(
            "Turn on parental mode", "Choose a password. It will be needed to add, edit, pause, resume or delete a schedule.");
        if (password is null) return;

        var (errors, updated) = ParentalMode.Enable(services.Settings, password);
        if (errors.Count > 0)
        {
            Notice = string.Join(" ", errors);
            return;
        }
        services.SaveSettings(updated);
        OnPropertyChanged(nameof(ParentalModeEnabled));
        OnPropertyChanged(nameof(LockLabel));
        OnPropertyChanged(nameof(IsLocked));
        OnPropertyChanged(nameof(HeaderLockText));
        IsUnlocked = true;
        Notice = "Parental mode is on.";
    }

    [RelayCommand]
    async Task TurnOffParentalMode()
    {
        var password = await passwordPrompt.ConfirmAsync(
            "Turn off parental mode", "Enter the current password.", pwd => ParentalMode.Verify(services.Settings, pwd));
        if (password is null) return;

        var (errors, updated) = ParentalMode.Disable(services.Settings, password);
        if (errors.Count > 0)
        {
            Notice = string.Join(" ", errors);
            return;
        }
        services.SaveSettings(updated);
        OnPropertyChanged(nameof(ParentalModeEnabled));
        OnPropertyChanged(nameof(LockLabel));
        OnPropertyChanged(nameof(IsLocked));
        OnPropertyChanged(nameof(HeaderLockText));
        IsUnlocked = false;
        Notice = "Parental mode is off.";
    }

    [RelayCommand]
    async Task ChangeParentalPassword()
    {
        var current = await passwordPrompt.ConfirmAsync(
            "Change password", "Enter the current password.", pwd => ParentalMode.Verify(services.Settings, pwd));
        if (current is null) return;

        var next = await passwordPrompt.SetAsync("Change password", "Choose a new password.");
        if (next is null) return;

        var (errors, updated) = ParentalMode.ChangePassword(services.Settings, current, next);
        if (errors.Count > 0)
        {
            Notice = string.Join(" ", errors);
            return;
        }
        services.SaveSettings(updated);
        Notice = "Password changed.";
    }

    // ---------- Settings ----------

    public bool CanStartAtLogin => services.LoginItem is not null;

    [ObservableProperty] bool settingDryRun;
    [ObservableProperty] decimal settingGraceSeconds;
    [ObservableProperty] bool settingCheckForUpdates;
    [ObservableProperty] bool settingStartAtLogin;

    void LoadSettings()
    {
        loadingSettings = true;
        SettingDryRun = services.Settings.DryRun;
        SettingGraceSeconds = services.Settings.ShutdownGraceSeconds;
        SettingCheckForUpdates = services.Settings.CheckForUpdates;
        SettingStartAtLogin = services.LoginItem?.IsEnabled == true;
        loadingSettings = false;
    }

    partial void OnSettingDryRunChanged(bool value) => SaveSettings();
    partial void OnSettingGraceSecondsChanged(decimal value) => SaveSettings();

    partial void OnSettingCheckForUpdatesChanged(bool value)
    {
        SaveSettings();
        if (value && !loadingSettings) _ = Updater.CheckAsync();
    }

    partial void OnSettingStartAtLoginChanged(bool value)
    {
        if (loadingSettings) return;
        try
        {
            services.LoginItem?.SetEnabled(value);
        }
        catch (Exception e)
        {
            services.Log.Write($"Start at sign-in failed: {e}");
            Notice = $"Could not change start at sign-in: {e.Message}";
        }
    }

    void SaveSettings()
    {
        if (loadingSettings) return;
        services.SaveSettings(services.Settings with
        {
            DryRun = SettingDryRun,
            ShutdownGraceSeconds = (int)Math.Clamp(SettingGraceSeconds, 0, 600),
            CheckForUpdates = SettingCheckForUpdates,
        });
    }

    [RelayCommand]
    Task CheckForUpdates() => Updater.CheckAsync();

    [RelayCommand]
    void RestartToUpdate() => Updater.RestartNow();

    [RelayCommand]
    void OpenDataFolder() => Open(services.DataDirectory);

    [RelayCommand]
    void OpenWebsite() => Open(AppInfo.WebsiteUrl);

    [RelayCommand]
    void ReportBug() => Open(AppInfo.BugReportUrl);

    static void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });

    public void Dispose() => clock.Stop();
}
