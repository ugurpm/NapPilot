using System.Globalization;
using PowerPlan.Core;

namespace PowerPlan.Platform.Mac;

/// <summary>Asks with an AppleScript dialog, the same mechanism <see cref="MacPowerController"/> uses for its grace warning.</summary>
public sealed class MacActivityPrompt : IActivityPrompt
{
    const int PromptTimeoutSeconds = 120;
    const int DurationPromptTimeoutSeconds = 60;

    static readonly (string Label, TimeSpan Duration)[] PostponeOptions =
    [
        ("10 minutes", TimeSpan.FromMinutes(10)),
        ("30 minutes", TimeSpan.FromMinutes(30)),
        ("1 hour", TimeSpan.FromHours(1)),
    ];

    readonly ICommandRunner runner;
    readonly ILog log;

    public MacActivityPrompt(ILog log) : this(log, ProcessCommandRunner.Instance)
    {
    }

    internal MacActivityPrompt(ILog log, ICommandRunner runner)
    {
        this.log = log;
        this.runner = runner;
    }

    public PostponeDecision Ask(Schedule schedule, DateTimeOffset scheduledFor)
    {
        var proceedButton = ProceedLabel(schedule.Action);
        var message = $"NapPilot was about to {Verb(schedule.Action)} for the schedule '{schedule.Name}', but you're using this Mac right now.";
        var result = AppleScript.Run(runner,
            $"display dialog {AppleScript.Quote(message)} with title \"NapPilot\" " +
            $"buttons {{\"Cancel for today\", \"Postpone\", {AppleScript.Quote(proceedButton)}}} default button 2 cancel button 1 " +
            $"with icon caution giving up after {PromptTimeoutSeconds.ToString(CultureInfo.InvariantCulture)}");

        if (AppleScript.WasCanceled(result))
        {
            log.Write($"'{schedule.Name}': the user cancelled today's run");
            return new(PostponeChoice.CancelledForToday);
        }
        if (!result.Succeeded || GaveUp(result))
        {
            log.Write($"'{schedule.Name}': no answer to the activity prompt, postponing");
            return new(PostponeChoice.Postpone, ScheduledRunner.DefaultPostpone);
        }

        var choice = ButtonReturned(result.Text);
        if (choice == proceedButton) return new(PostponeChoice.Proceed);

        var duration = AskPostponeDuration(schedule);
        log.Write($"'{schedule.Name}': postponing by {duration}");
        return new(PostponeChoice.Postpone, duration);
    }

    TimeSpan AskPostponeDuration(Schedule schedule)
    {
        var result = AppleScript.Run(runner,
            $"display dialog {AppleScript.Quote($"Postpone '{schedule.Name}' for how long?")} with title \"NapPilot\" " +
            $"buttons {{{string.Join(", ", PostponeOptions.Select(o => AppleScript.Quote(o.Label)))}}} default button 1 " +
            $"with icon note giving up after {DurationPromptTimeoutSeconds.ToString(CultureInfo.InvariantCulture)}");

        if (result.Succeeded && !GaveUp(result))
        {
            var choice = ButtonReturned(result.Text);
            foreach (var option in PostponeOptions)
                if (option.Label == choice) return option.Duration;
        }
        return ScheduledRunner.DefaultPostpone;
    }

    internal static bool GaveUp(CommandResult result) => result.Text.Contains("gave up:true", StringComparison.Ordinal);

    /// <summary>Parses osascript's <c>button returned:X, gave up:false</c> reply.</summary>
    internal static string ButtonReturned(string text)
    {
        const string marker = "button returned:";
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return "";
        start += marker.Length;
        var end = text.IndexOf(", gave up:", start, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }

    static string Verb(PowerAction action) => action switch
    {
        PowerAction.Shutdown => "shut down",
        PowerAction.Restart => "restart",
        PowerAction.Sleep => "put your Mac to sleep",
        _ => "run the schedule",
    };

    static string ProceedLabel(PowerAction action) => action switch
    {
        PowerAction.Shutdown => "Shut down now",
        PowerAction.Restart => "Restart now",
        PowerAction.Sleep => "Sleep now",
        _ => "Continue",
    };
}
