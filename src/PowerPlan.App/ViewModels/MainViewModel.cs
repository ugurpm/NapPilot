using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PowerPlan.Core;

namespace PowerPlan.App.ViewModels;

public sealed record ScheduleRow(Schedule Schedule, string Title, string Detail);

public partial class MainViewModel : ObservableObject
{
    const string Once = "Once";

    static readonly Dictionary<string, Recurrence> Repeats = new()
    {
        ["Every day"] = Recurrence.Daily,
        ["Weekdays"] = Recurrence.Weekdays,
        ["Weekends"] = Recurrence.Weekends,
    };

    readonly AppServices services;

    public MainViewModel(AppServices services)
    {
        this.services = services;
        var wake = WakeAdvisor.Assess(services.Capabilities);
        WakeNotes = wake.CanWake
            ? wake.Notes
            : ["Scheduled wake is not available on this PC.", .. wake.Notes];
        Refresh();
    }

    public ObservableCollection<ScheduleRow> Schedules { get; } = [];
    public IReadOnlyList<PowerAction> Actions { get; } = Enum.GetValues<PowerAction>();
    public IReadOnlyList<string> RepeatOptions { get; } = [Once, .. Repeats.Keys];
    public IReadOnlyList<string> WakeNotes { get; }
    public bool DryRun => services.Settings.DryRun;
    public string DataDirectory => services.DataDirectory;

    [ObservableProperty] string newName = "";
    [ObservableProperty] PowerAction newAction = PowerAction.Wake;
    [ObservableProperty] string newTime = "07:00";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsOnce))] string newRepeat = "Every day";
    [ObservableProperty] DateTime? newDate = DateTime.Today.AddDays(1);
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasError))] string? error;

    public bool IsOnce => NewRepeat == Once;
    public bool HasError => Error is not null;

    [RelayCommand]
    void Add()
    {
        if (!TimeOnly.TryParseExact(NewTime.Trim(), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            Error = "Enter the time as HH:mm, for example 07:30.";
            return;
        }
        if (IsOnce && NewDate is null)
        {
            Error = "Pick a date.";
            return;
        }

        var schedule = new Schedule
        {
            Name = NewName.Trim(),
            Action = NewAction,
            Time = time,
            Recurrence = IsOnce ? Recurrence.Once(DateOnly.FromDateTime(NewDate!.Value)) : Repeats[NewRepeat],
        };

        try
        {
            var errors = services.Schedules.Save(schedule);
            Error = errors.Count > 0 ? string.Join(" ", errors) : null;
            if (errors.Count == 0) NewName = "";
        }
        catch (Exception e)
        {
            services.Log.Write($"Saving schedule failed: {e}");
            Error = $"Could not register the schedule with Windows: {e.Message}";
        }
        Refresh();
    }

    [RelayCommand]
    void Remove(ScheduleRow row)
    {
        services.Schedules.Remove(row.Schedule.Id);
        Refresh();
    }

    void Refresh()
    {
        Schedules.Clear();
        foreach (var s in services.Schedules.All().OrderBy(s => services.Schedules.NextRun(s) ?? DateTimeOffset.MaxValue))
        {
            var next = services.Schedules.NextRun(s) is { } n ? $"next {n.ToLocalTime():ddd d MMM HH:mm}" : "done";
            Schedules.Add(new ScheduleRow(s, $"{s.Name}  ·  {s.Action}", $"{s.Time:HH:mm}  ·  {s.Recurrence}  ·  {next}"));
        }
    }
}
