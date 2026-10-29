using System.Globalization;
using PowerPlan.Core;

namespace PowerPlan.App.ViewModels;

/// <summary>One row of the schedule list, with everything the template shows already formatted.</summary>
public sealed class EntryRow(ScheduleEntry entry, DateTimeOffset now)
{
    public ScheduleEntry Entry => entry;
    public ActionStyle Style => ActionStyle.Of(entry.Action);

    public bool IsPast => entry.Kind == EntryKind.Past;
    public bool IsPaused => entry.Kind == EntryKind.Paused;
    public bool IsUpcoming => entry.Kind == EntryKind.Upcoming;

    public string Name => entry.Name;

    public string KindLabel => entry.Kind switch
    {
        EntryKind.Upcoming => "UPCOMING",
        EntryKind.Paused => "PAUSED",
        _ => "PAST",
    };

    public string WhenText => entry.When is { } when ? when.ToLocalTime().ToString("ddd d MMM yyyy · HH:mm", CultureInfo.CurrentCulture) : "";

    public string RelativeText => entry.When is { } when ? Relative(when - now) : "";

    /// <summary>Upcoming: how it repeats. Past: what happened.</summary>
    public string DetailText => IsPast
        ? entry.Outcome
        : entry.Schedule?.Recurrence.ToString() ?? "";

    public bool CanEdit => entry.Schedule is not null;
    public bool CanToggle => !IsPast;
    public string ToggleLabel => IsPaused ? "Resume" : "Pause";
    public double RowOpacity => IsPast ? 0.72 : IsPaused ? 0.6 : 1;

    public bool Matches(string filter, PowerAction? action, string search) =>
        (filter == "All" || (filter == "Upcoming" ? !IsPast : IsPast))
        && (action is null || entry.Action == action)
        && (search.Length == 0 || entry.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase));

    internal static string Relative(TimeSpan span)
    {
        var future = span >= TimeSpan.Zero;
        var d = span.Duration();
        var text = d.TotalMinutes < 1 ? "less than a minute"
            : d.TotalHours < 1 ? $"{(int)d.TotalMinutes} min"
            : d.TotalDays < 1 ? $"{(int)d.TotalHours} h {d.Minutes} min"
            : d.TotalDays < 2 ? $"1 day {d.Hours} h"
            : $"{(int)d.TotalDays} days";
        return future ? $"in {text}" : $"{text} ago";
    }
}
