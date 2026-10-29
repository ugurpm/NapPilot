using Avalonia.Media;
using Avalonia.Media.Immutable;
using PowerPlan.Core;

namespace PowerPlan.App.ViewModels;

/// <summary>Colour and icon per action, shared by the action picker and the list so each action reads the same everywhere.</summary>
public sealed record ActionStyle(PowerAction Action, string Label, IBrush Brush, IBrush SoftBrush, Geometry Icon)
{
    static ActionStyle Make(PowerAction action, string label, uint rgb, string icon)
    {
        var color = Color.FromUInt32(0xFF000000 | rgb);
        return new(action, label, new ImmutableSolidColorBrush(color), new ImmutableSolidColorBrush(color, 0.16),
            StreamGeometry.Parse(icon));
    }

    public static readonly IReadOnlyList<ActionStyle> All =
    [
        Make(PowerAction.Wake, "Wake", 0x10B981,
            "M12 7a5 5 0 1 1 0 10a5 5 0 0 1 0-10z M11 1h2v3h-2z M11 20h2v3h-2z M1 11h3v2H1z M20 11h3v2h-3z " +
            "M4.2 5.6l1.4-1.4l2.1 2.1l-1.4 1.4z M16.3 17.7l1.4-1.4l2.1 2.1l-1.4 1.4z M16.3 6.3l2.1-2.1l1.4 1.4l-2.1 2.1z M4.2 18.4l2.1-2.1l1.4 1.4l-2.1 2.1z"),
        Make(PowerAction.Sleep, "Sleep", 0x6366F1, "M20.5 14.6A8.6 8.6 0 1 1 9.4 3.5a7 7 0 0 0 11.1 11.1z"),
        Make(PowerAction.Restart, "Restart", 0xF59E0B,
            "M12 4V1L7 5.5L12 10V6.5A5.5 5.5 0 1 1 6.5 12H4A8 8 0 1 0 12 4z"),
        Make(PowerAction.Shutdown, "Shut down", 0xEF4444,
            "M11 2h2v10h-2z M7.05 5.05l1.42 1.42A6.5 6.5 0 1 0 15.53 6.47l1.42-1.42A8.5 8.5 0 1 1 7.05 5.05z"),
    ];

    public static ActionStyle Of(PowerAction action) => All.First(s => s.Action == action);
}
