namespace PowerPlan.Core;

public enum PowerAction
{
    Shutdown,
    Restart,
    /// <summary>Sleep. On Modern Standby hardware this hibernates so a later wake timer can fire.</summary>
    Sleep,
    /// <summary>Wake the PC and turn the screen on.</summary>
    Wake,
}

/// <summary>When a schedule repeats: once on a date, or on chosen days of the week.</summary>
public sealed record Recurrence
{
    public DateOnly? Date { get; init; }
    public DayOfWeek[] Days { get; init; } = [];

    public bool IsOnce => Date is not null;

    public static Recurrence Once(DateOnly date) => new() { Date = date };

    public static Recurrence Weekly(params DayOfWeek[] days) => new() { Days = days.Distinct().Order().ToArray() };

    public static Recurrence Daily => Weekly(Enum.GetValues<DayOfWeek>());

    public static Recurrence Weekdays =>
        Weekly(DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday);

    public static Recurrence Weekends => Weekly(DayOfWeek.Saturday, DayOfWeek.Sunday);

    public override string ToString()
    {
        if (Date is { } date) return $"Once on {date:yyyy-MM-dd}";
        if (Days.Length == 7) return "Every day";
        if (Days.SequenceEqual(Weekdays.Days)) return "Weekdays";
        if (Days.SequenceEqual(Weekends.Days)) return "Weekends";
        return string.Join(", ", Days.Select(d => d.ToString()[..3]));
    }
}

public sealed record Schedule
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public PowerAction Action { get; init; }
    public TimeOnly Time { get; init; }
    public Recurrence Recurrence { get; init; } = Recurrence.Daily;
    public bool Enabled { get; init; } = true;
}
