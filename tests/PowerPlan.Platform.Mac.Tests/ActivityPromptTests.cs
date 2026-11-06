using PowerPlan.Core;
using PowerPlan.Platform.Mac;

namespace PowerPlan.Platform.Mac.Tests;

public class ActivityPromptTests
{
    readonly FakeRunner runner = new();
    readonly ListLog log = new();

    MacActivityPrompt Prompt() => new(log, runner);

    static Schedule Shutdown => new() { Name = "Night", Action = PowerAction.Shutdown };

    [Fact]
    public void Proceed_button_runs_now()
    {
        runner.Respond = (file, args) => file == Tools.Osascript ? new(0, "button returned:Shut down now, gave up:false", "") : null;

        var decision = Prompt().Ask(Shutdown, DateTimeOffset.UtcNow);

        Assert.Equal(PostponeChoice.Proceed, decision.Choice);
    }

    [Fact]
    public void Cancel_for_today_maps_to_cancelled()
    {
        runner.Respond = (file, args) => file == Tools.Osascript
            ? new(1, "", "execution error: User canceled. (-128)")
            : null;

        var decision = Prompt().Ask(Shutdown, DateTimeOffset.UtcNow);

        Assert.Equal(PostponeChoice.CancelledForToday, decision.Choice);
        Assert.Contains(log.Lines, l => l.Contains("cancelled"));
    }

    [Fact]
    public void Postpone_then_duration_choice_is_honoured()
    {
        runner.Respond = (file, args) =>
        {
            if (file != Tools.Osascript) return null;
            return args[1].Contains("for how long?")
                ? new(0, "button returned:30 minutes, gave up:false", "")
                : new(0, "button returned:Postpone, gave up:false", "");
        };

        var decision = Prompt().Ask(Shutdown, DateTimeOffset.UtcNow);

        Assert.Equal(PostponeChoice.Postpone, decision.Choice);
        Assert.Equal(TimeSpan.FromMinutes(30), decision.PostponeBy);
    }

    [Fact]
    public void No_answer_postpones_by_the_default_amount()
    {
        runner.Respond = (file, args) => file == Tools.Osascript ? new(0, "button returned:Cancel for today, gave up:true", "") : null;

        var decision = Prompt().Ask(Shutdown, DateTimeOffset.UtcNow);

        Assert.Equal(PostponeChoice.Postpone, decision.Choice);
        Assert.Equal(ScheduledRunner.DefaultPostpone, decision.PostponeBy);
        Assert.Contains(log.Lines, l => l.Contains("no answer"));
    }

    [Fact]
    public void No_answer_to_the_duration_prompt_uses_the_default_too()
    {
        runner.Respond = (file, args) =>
        {
            if (file != Tools.Osascript) return null;
            return args[1].Contains("for how long?")
                ? new(0, "button returned:10 minutes, gave up:true", "")
                : new(0, "button returned:Postpone, gave up:false", "");
        };

        var decision = Prompt().Ask(Shutdown, DateTimeOffset.UtcNow);

        Assert.Equal(ScheduledRunner.DefaultPostpone, decision.PostponeBy);
    }

    [Fact]
    public void Dialog_names_the_schedule_and_the_right_verb()
    {
        runner.Respond = (file, args) => file == Tools.Osascript ? new(0, "button returned:Restart now, gave up:false", "") : null;

        Prompt().Ask(new Schedule { Name = "Nightly build", Action = PowerAction.Restart }, DateTimeOffset.UtcNow);

        var script = runner.Scripts.First();
        Assert.Contains("restart", script);
        Assert.Contains("'Nightly build'", script);
        Assert.Contains("\"Restart now\"", script);
    }
}
