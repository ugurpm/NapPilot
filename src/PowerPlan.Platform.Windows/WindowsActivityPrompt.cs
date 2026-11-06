using System.Windows.Forms;
using PowerPlan.Core;

namespace PowerPlan.Platform.Windows;

/// <summary>Asks with a native task dialog, so it appears regardless of whether the tray window is open or even running.</summary>
public sealed class WindowsActivityPrompt(ILog log) : IActivityPrompt
{
    static readonly TimeSpan PromptTimeout = TimeSpan.FromMinutes(2);

    static readonly (string Label, TimeSpan Duration)[] PostponeOptions =
    [
        ("10 minutes", TimeSpan.FromMinutes(10)),
        ("30 minutes", TimeSpan.FromMinutes(30)),
        ("1 hour", TimeSpan.FromHours(1)),
    ];

    public PostponeDecision Ask(Schedule schedule, DateTimeOffset scheduledFor)
    {
        var postpone = new TaskDialogButton("Postpone…");
        var cancelToday = new TaskDialogButton("Cancel for today");
        var proceed = new TaskDialogButton(ProceedLabel(schedule.Action));

        var page = new TaskDialogPage
        {
            Caption = "NapPilot",
            Heading = "You're using this computer",
            Text = $"NapPilot was about to {Verb(schedule.Action)} for the schedule '{schedule.Name}'.",
            Icon = TaskDialogIcon.Warning,
            Buttons = { postpone, cancelToday, proceed },
            DefaultButton = postpone,
        };

        var timedOut = ArmTimeout(page);
        var clicked = TaskDialog.ShowDialog(page);

        if (timedOut())
        {
            log.Write($"'{schedule.Name}': no answer to the activity prompt, postponing");
            return new(PostponeChoice.Postpone, ScheduledRunner.DefaultPostpone);
        }
        if (clicked == cancelToday)
        {
            log.Write($"'{schedule.Name}': the user cancelled today's run");
            return new(PostponeChoice.CancelledForToday);
        }
        if (clicked == proceed) return new(PostponeChoice.Proceed);

        var duration = AskPostponeDuration(schedule);
        log.Write($"'{schedule.Name}': postponing by {duration}");
        return new(PostponeChoice.Postpone, duration);
    }

    TimeSpan AskPostponeDuration(Schedule schedule)
    {
        var buttons = PostponeOptions.Select(o => new TaskDialogButton(o.Label)).ToArray();
        var page = new TaskDialogPage
        {
            Caption = "NapPilot",
            Heading = "Postpone for how long?",
            Text = $"'{schedule.Name}' will be tried again after that, in case you're still busy.",
            Icon = TaskDialogIcon.Information,
            Buttons = { buttons[0], buttons[1], buttons[2] },
            DefaultButton = buttons[0],
        };

        var timedOut = ArmTimeout(page);
        var clicked = TaskDialog.ShowDialog(page);

        if (!timedOut())
            for (var i = 0; i < buttons.Length; i++)
                if (clicked == buttons[i]) return PostponeOptions[i].Duration;

        return ScheduledRunner.DefaultPostpone;
    }

    /// <summary>Closes the dialog after <see cref="PromptTimeout"/> with no answer; returns whether that happened.</summary>
    static Func<bool> ArmTimeout(TaskDialogPage page)
    {
        var timedOut = false;
        var timer = new System.Windows.Forms.Timer { Interval = (int)PromptTimeout.TotalMilliseconds };
        timer.Tick += (_, _) =>
        {
            timedOut = true;
            timer.Stop();
            page.BoundDialog?.Close();
        };
        page.Created += (_, _) => timer.Start();
        page.Destroyed += (_, _) => timer.Dispose();
        return () => timedOut;
    }

    static string Verb(PowerAction action) => action switch
    {
        PowerAction.Shutdown => "shut down the computer",
        PowerAction.Restart => "restart the computer",
        PowerAction.Sleep => "put the computer to sleep",
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
