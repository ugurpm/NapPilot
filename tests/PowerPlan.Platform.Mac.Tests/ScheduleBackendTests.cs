using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using PowerPlan.Core;
using PowerPlan.Platform.Mac;

namespace PowerPlan.Platform.Mac.Tests;

public class LaunchJobTests
{
    static readonly Schedule Shutdown = new() { Name = "Night", Action = PowerAction.Shutdown, Time = new(1, 30) };
    // 22:30 UTC on 1 November is 01:30 on 2 November in Istanbul.
    static readonly DateTimeOffset RunAt = new(2026, 11, 1, 22, 30, 0, TimeSpan.Zero);

    static XElement Value(XDocument doc, string key) =>
        doc.Root!.Element("dict")!.Elements("key").Single(k => k.Value == key).ElementsAfterSelf().First();

    static Dictionary<string, string> Dict(XElement dict) =>
        dict.Elements("key").ToDictionary(k => k.Value, k => ((XElement)k.NextNode!).Value);

    [Fact]
    public void Label_is_per_schedule_and_run_time()
    {
        var id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
        Assert.Equal("com.nappilot.run.0f8fad5bd9cb469fa16570867728950e.1793572200", LaunchJob.Label(id, RunAt));
        Assert.StartsWith(LaunchJob.Prefix(id), LaunchJob.Label(id, RunAt));
    }

    [Fact]
    public void Runs_the_app_with_the_schedule_id_and_due_time()
    {
        var doc = LaunchJob.Build(Shutdown, RunAt, "/Applications/NapPilot.app/Contents/MacOS/NapPilot", Zones.Istanbul);

        Assert.Equal(LaunchJob.Label(Shutdown.Id, RunAt), Value(doc, "Label").Value);
        Assert.Equal(
            ["/Applications/NapPilot.app/Contents/MacOS/NapPilot", "--run", Shutdown.Id.ToString(), "--at", "1793572200"],
            Value(doc, "ProgramArguments").Elements("string").Select(e => e.Value));
        Assert.Equal(RunAt, DateTimeOffset.FromUnixTimeSeconds(1793572200));
    }

    [Fact]
    public void Calendar_interval_is_local_wall_time()
    {
        var interval = Value(LaunchJob.Build(Shutdown, RunAt, "/x", Zones.Istanbul), "StartCalendarInterval");

        Assert.Equal(new Dictionary<string, string> { ["Month"] = "11", ["Day"] = "2", ["Hour"] = "1", ["Minute"] = "30" }, Dict(interval));
        Assert.All(interval.Elements().Where(e => e.Name != "key"), e => Assert.Equal("integer", e.Name.LocalName));
    }

    [Fact]
    public void Job_knows_its_own_label()
    {
        var doc = LaunchJob.Build(Shutdown, RunAt, "/x", Zones.Istanbul);
        Assert.Equal(LaunchJob.Label(Shutdown.Id, RunAt), Dict(Value(doc, "EnvironmentVariables"))[LaunchJob.EnvironmentVariable]);
    }

    [Fact]
    public void Plist_text_has_the_apple_doctype_and_escapes_values()
    {
        var text = Plist.ToText(LaunchJob.Build(Shutdown, RunAt, "/Users/a&b/<NapPilot>", Zones.Istanbul));

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", text);
        Assert.Contains("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">", text);
        Assert.Contains("<plist version=\"1.0\">", text);
        Assert.Contains("<string>/Users/a&amp;b/&lt;NapPilot&gt;</string>", text);
        Assert.False(text.StartsWith('\uFEFF'));
    }
}

public class PmsetDateTests
{
    [Fact]
    public void Formats_local_wall_time_regardless_of_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            // Arabic (Saudi Arabia) uses another calendar and digits; pmset must still get MM/dd/yy.
            CultureInfo.CurrentCulture = new CultureInfo("ar-SA");
            Assert.Equal("11/02/26 01:29:00", PmsetDate.Format(new DateTimeOffset(2026, 11, 1, 22, 29, 0, TimeSpan.Zero), Zones.Istanbul));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}

public sealed class MacScheduleBackendTests : IDisposable
{
    const string Exe = "/Applications/NapPilot.app/Contents/MacOS/NapPilot";
    static DateTimeOffset Local(int month, int day, int hour, int minute = 0, int second = 0) =>
        new(2026, month, day, hour, minute, second, TimeSpan.FromHours(3));

    static readonly Schedule DailyWake = new() { Name = "Morning", Action = PowerAction.Wake, Time = new(7, 0), Recurrence = Recurrence.Daily };

    readonly TempDir data = new();
    readonly TempDir agents = new();
    readonly FakeRunner runner = new();
    readonly ListLog log = new();
    readonly FixedTime time = new(Local(10, 29, 12), Zones.Istanbul);

    public void Dispose()
    {
        data.Dispose();
        agents.Dispose();
    }

    MacScheduleBackend Backend(string? currentJob = null) =>
        new(Exe, data.Path, log, runner, agents.Path, time, currentJob);

    string[] PlistFiles(Guid id) => Directory.GetFiles(agents.Path, $"{LaunchJob.Prefix(id)}*.plist").Select(Path.GetFileName).ToArray()!;

    static string[] Commands(string script, string kind) =>
        Regex.Matches(script, $@"/usr/bin/pmset schedule {kind}'([^']+)'").Select(m => m.Groups[1].Value).ToArray();

    static string[] Adds(string script) => Commands(script, "wake ");
    static string[] Cancels(string script) => Commands(script, "cancel wake ");

    [Fact]
    public void Register_writes_and_loads_one_launch_agent()
    {
        var schedule = DailyWake with { Action = PowerAction.Shutdown };
        Backend().Register(schedule, Local(10, 30, 7));

        var label = LaunchJob.Label(schedule.Id, Local(10, 30, 7));
        Assert.Equal([$"{label}.plist"], PlistFiles(schedule.Id));
        var bootstrap = Assert.Single(runner.Launchctl("bootstrap"));
        Assert.Equal(["bootstrap", "gui/501", Path.Combine(agents.Path, $"{label}.plist")], bootstrap[1..]);
        Assert.Empty(runner.Scripts);
    }

    [Fact]
    public void Re_registering_the_same_run_does_not_reload_the_job()
    {
        var schedule = DailyWake with { Action = PowerAction.Sleep };
        Backend().Register(schedule, Local(10, 30, 7));
        Backend().Register(schedule, Local(10, 30, 7));

        Assert.Single(runner.Launchctl("bootstrap"));
        Assert.Empty(runner.Launchctl("bootout"));
    }

    [Fact]
    public void Re_arming_replaces_the_previous_job()
    {
        var schedule = DailyWake with { Action = PowerAction.Sleep };
        Backend().Register(schedule, Local(10, 30, 7));
        Backend().Register(schedule, Local(10, 31, 7));

        var old = LaunchJob.Label(schedule.Id, Local(10, 30, 7));
        Assert.Equal([$"{LaunchJob.Label(schedule.Id, Local(10, 31, 7))}.plist"], PlistFiles(schedule.Id));
        Assert.Equal(["bootout", $"gui/501/{old}"], Assert.Single(runner.Launchctl("bootout"))[1..]);
    }

    [Fact]
    public void Re_arming_from_inside_the_running_job_does_not_stop_it()
    {
        var schedule = DailyWake with { Action = PowerAction.Shutdown };
        Backend().Register(schedule, Local(10, 30, 7));
        var current = LaunchJob.Label(schedule.Id, Local(10, 30, 7));

        time.Now = Local(10, 30, 7, 0, 5);
        Backend(currentJob: current).Register(schedule, Local(10, 31, 7));

        Assert.Empty(runner.Launchctl("bootout"));
        Assert.Contains($"{current}.plist", PlistFiles(schedule.Id));
        Assert.Contains(current, runner.Loaded);

        // The next arm outside that job cleans it up.
        Backend().Register(schedule, Local(10, 31, 7));
        Assert.Equal([$"{LaunchJob.Label(schedule.Id, Local(10, 31, 7))}.plist"], PlistFiles(schedule.Id));
    }

    [Fact]
    public void A_job_that_is_running_is_left_alone()
    {
        var schedule = DailyWake with { Action = PowerAction.Shutdown };
        Backend().Register(schedule, Local(10, 30, 7));
        runner.Running.Add(LaunchJob.Label(schedule.Id, Local(10, 30, 7)));

        Backend().Register(schedule, Local(10, 31, 7));

        Assert.Empty(runner.Launchctl("bootout"));
        Assert.Equal(2, PlistFiles(schedule.Id).Length);
    }

    [Fact]
    public void Failed_bootstrap_throws_and_leaves_no_plist()
    {
        runner.Respond = (file, args) => file == Tools.Launchctl && args[0] == "bootstrap" ? new(5, "", "Bootstrap failed: 5: Input/output error") : null;
        var schedule = DailyWake with { Action = PowerAction.Restart };

        var error = Assert.Throws<InvalidOperationException>(() => Backend().Register(schedule, Local(10, 30, 7)));
        Assert.Contains("Input/output error", error.Message);
        Assert.Empty(PlistFiles(schedule.Id));
    }

    [Fact]
    public void Unregister_boots_out_and_deletes_every_job_of_the_schedule()
    {
        var schedule = DailyWake with { Action = PowerAction.Shutdown };
        var other = schedule with { Id = Guid.NewGuid() };
        Backend().Register(schedule, Local(10, 30, 7));
        Backend().Register(other, Local(10, 30, 7));

        Backend().Unregister(schedule.Id);

        Assert.Empty(PlistFiles(schedule.Id));
        Assert.Single(PlistFiles(other.Id));
        Assert.Equal(["bootout", $"gui/501/{LaunchJob.Label(schedule.Id, Local(10, 30, 7))}"], Assert.Single(runner.Launchctl("bootout"))[1..]);
    }

    [Fact]
    public void Unregister_with_no_recorded_wakes_does_not_prompt()
    {
        Backend().Unregister(Guid.NewGuid());
        Assert.Empty(runner.Scripts);
    }

    [Fact]
    public void First_wake_schedules_30_days_of_wakes_in_one_prompt()
    {
        Backend().Register(DailyWake, Local(10, 30, 7));

        var script = Assert.Single(runner.Scripts);
        Assert.Contains("with administrator privileges", script);
        var adds = Adds(script);
        // 30 October to 29 November inclusive, each a minute before 07:00.
        Assert.Equal(31, adds.Length);
        Assert.Equal("10/30/26 06:59:00", adds[0]);
        Assert.Equal("11/29/26 06:59:00", adds[^1]);
        Assert.Empty(Cancels(script));
    }

    [Fact]
    public void Wakes_are_scheduled_before_the_launch_agent()
    {
        Backend().Register(DailyWake, Local(10, 30, 7));
        var osascript = runner.Calls.FindIndex(c => c[0] == Tools.Osascript);
        var bootstrap = runner.Calls.FindIndex(c => c[0] == Tools.Launchctl && c[1] == "bootstrap");
        Assert.True(osascript < bootstrap);
    }

    [Fact]
    public void Re_arming_a_covered_wake_from_the_run_job_does_not_prompt()
    {
        Backend().Register(DailyWake, Local(10, 30, 7));
        var job = LaunchJob.Label(DailyWake.Id, Local(10, 30, 7));

        for (var day = 30; day <= 31; day++)
        {
            time.Now = Local(10, day, 7, 0, 5);
            Backend(currentJob: job).Register(DailyWake, Local(10, day, 7).AddDays(1));
            job = LaunchJob.Label(DailyWake.Id, Local(10, day, 7).AddDays(1));
        }

        Assert.Single(runner.Scripts);
    }

    [Fact]
    public void Re_arming_at_startup_with_plenty_of_wakes_left_does_not_prompt()
    {
        Backend().Register(DailyWake, Local(10, 30, 7));
        time.Now = Local(11, 5, 9);
        Backend().Register(DailyWake, Local(11, 6, 7));
        Assert.Single(runner.Scripts);
    }

    [Fact]
    public void Wakes_running_low_are_topped_up_outside_a_run_job_only()
    {
        Backend().Register(DailyWake, Local(10, 30, 7));
        time.Now = Local(11, 25, 9);

        Backend(currentJob: "com.nappilot.run.x.1").Register(DailyWake, Local(11, 26, 7));
        Assert.Single(runner.Scripts);

        Backend().Register(DailyWake, Local(11, 26, 7));
        var topUp = runner.Scripts.Last();
        Assert.Equal(2, runner.Scripts.Count());
        Assert.Empty(Cancels(topUp));
        // Already scheduled up to 29 November; the top-up adds 30 November to 26 December.
        Assert.Equal("11/30/26 06:59:00", Adds(topUp)[0]);
        Assert.Equal("12/26/26 06:59:00", Adds(topUp)[^1]);
    }

    [Fact]
    public void Running_out_of_wakes_prompts_even_inside_a_run_job()
    {
        Backend().Register(DailyWake, Local(10, 30, 7));
        time.Now = Local(11, 29, 7, 0, 5);
        Backend(currentJob: "com.nappilot.run.x.1").Register(DailyWake, Local(11, 30, 7));
        Assert.Equal(2, runner.Scripts.Count());
    }

    [Fact]
    public void Changing_the_time_cancels_the_old_wakes_in_the_same_prompt()
    {
        Backend().Register(DailyWake, Local(10, 30, 7));
        var later = DailyWake with { Time = new(8, 0) };

        Backend().Register(later, Local(10, 30, 8));

        Assert.Equal(2, runner.Scripts.Count());
        var script = runner.Scripts.Last();
        Assert.Equal(31, Cancels(script).Length);
        Assert.Equal("10/30/26 06:59:00", Cancels(script)[0]);
        Assert.Equal("10/30/26 07:59:00", Adds(script)[0]);
        Assert.Contains("NapPilot || true &&", script);
    }

    [Fact]
    public void Dropping_days_cancels_just_those_wakes()
    {
        Backend().Register(DailyWake, Local(10, 30, 7)); // a Friday
        var weekdays = DailyWake with { Recurrence = Recurrence.Weekdays };

        Backend().Register(weekdays, Local(10, 30, 7));

        var script = runner.Scripts.Last();
        Assert.Empty(Adds(script));
        Assert.Equal("10/31/26 06:59:00", Cancels(script)[0]); // Saturday
        Assert.Equal(10, Cancels(script).Length); // the five weekends up to 29 November
    }

    [Fact]
    public void Turning_a_wake_into_another_action_cancels_its_wakes()
    {
        Backend().Register(DailyWake, Local(10, 30, 7));
        Backend().Register(DailyWake with { Action = PowerAction.Shutdown }, Local(10, 30, 7));

        var script = runner.Scripts.Last();
        Assert.Equal(31, Cancels(script).Length);
        Assert.Empty(Adds(script));
    }

    [Fact]
    public void Unregister_cancels_recorded_wakes_in_one_prompt_and_forgets_them()
    {
        Backend().Register(DailyWake, Local(10, 30, 7));
        Backend().Unregister(DailyWake.Id);
        Backend().Unregister(DailyWake.Id);

        Assert.Equal(2, runner.Scripts.Count());
        Assert.Equal(31, Cancels(runner.Scripts.Last()).Length);
    }

    [Fact]
    public void A_wake_shared_with_another_schedule_is_not_cancelled()
    {
        var other = DailyWake with { Id = Guid.NewGuid(), Recurrence = Recurrence.Once(new DateOnly(2026, 10, 30)) };
        Backend().Register(other, Local(10, 30, 7));
        Backend().Register(DailyWake, Local(10, 30, 7));

        Assert.Equal(30, Adds(runner.Scripts.Last()).Length);

        Backend().Unregister(DailyWake.Id);
        Assert.DoesNotContain("10/30/26 06:59:00", Cancels(runner.Scripts.Last()));
    }

    [Fact]
    public void Cancelled_password_prompt_throws_and_changes_nothing()
    {
        runner.Respond = (file, _) => file == Tools.Osascript ? new(1, "", "0:94: execution error: User canceled. (-128)") : null;

        var error = Assert.Throws<InvalidOperationException>(() => Backend().Register(DailyWake, Local(10, 30, 7)));

        Assert.Contains("cancelled", error.Message);
        Assert.Empty(PlistFiles(DailyWake.Id));
        Assert.Empty(runner.Launchctl("bootstrap"));

        // Nothing was recorded, so the next attempt asks again.
        runner.Respond = (_, _) => null;
        Backend().Register(DailyWake, Local(10, 30, 7));
        Assert.Equal(31, Adds(runner.Scripts.Last()).Length);
    }

    [Fact]
    public void Wake_less_than_a_minute_away_is_not_scheduled_in_the_past()
    {
        time.Now = Local(10, 30, 6, 59, 30);
        var once = DailyWake with { Recurrence = Recurrence.Once(new DateOnly(2026, 10, 30)) };

        Backend().Register(once, Local(10, 30, 7));

        Assert.Empty(runner.Scripts);
        Assert.Single(PlistFiles(once.Id));
    }

    [Fact]
    public void Once_wake_schedules_a_single_wake()
    {
        var once = DailyWake with { Recurrence = Recurrence.Once(new DateOnly(2026, 11, 3)) };
        Backend().Register(once, Local(11, 3, 7));
        Assert.Equal(["11/03/26 06:59:00"], Adds(Assert.Single(runner.Scripts)));
    }
}

public class AppleScriptTests
{
    [Fact]
    public void Quote_escapes_backslashes_and_double_quotes()
    {
        Assert.Equal("\"say \\\"hi\\\" C:\\\\x\"", AppleScript.Quote("say \"hi\" C:\\x"));
    }

    [Fact]
    public void Cancel_is_recognised_by_its_error_number()
    {
        Assert.True(AppleScript.WasCanceled(new(1, "", "execution error: Benutzer hat abgebrochen. (-128)")));
        Assert.False(AppleScript.WasCanceled(new(1, "", "execution error: something else (-1743)")));
        Assert.False(AppleScript.WasCanceled(new(0, "button returned:Cancel (-128)", "")));
    }
}
