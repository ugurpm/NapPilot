using PowerPlan.Core;
using PowerPlan.Platform.Mac;

namespace PowerPlan.Platform.Mac.Tests;

public class MacPowerControllerTests
{
    readonly FakeRunner runner = new();
    readonly ListLog log = new();

    MacPowerController Power(int grace = 60) => new(grace, log, runner);

    bool RanSystemEvents(string command) => runner.Scripts.Contains($"tell application \"System Events\" to {command}");

    [Fact]
    public void Shutdown_warns_first_then_shuts_down()
    {
        Power().Shutdown();

        var dialog = runner.Scripts.First();
        Assert.Contains("display dialog \"NapPilot will shut down your Mac in 60 seconds.\"", dialog);
        Assert.Contains("buttons {\"Cancel\", \"Shut down now\"} default button 2", dialog);
        Assert.Contains("giving up after 60", dialog);
        Assert.True(RanSystemEvents("shut down"));
    }

    [Fact]
    public void Cancel_in_the_dialog_stops_the_shutdown()
    {
        runner.Respond = (file, args) => file == Tools.Osascript && args[1].StartsWith("display dialog")
            ? new(1, "", "execution error: User canceled. (-128)")
            : null;

        Power().Shutdown();
        Power().Restart();

        Assert.Equal(2, runner.Scripts.Count());
        Assert.All(runner.Scripts, s => Assert.StartsWith("display dialog", s));
        Assert.Contains(log.Lines, l => l.Contains("cancelled"));
    }

    [Fact]
    public void Restart_uses_its_own_wording()
    {
        Power(30).Restart();

        Assert.Contains("NapPilot will restart your Mac in 30 seconds.", runner.Scripts.First());
        Assert.Contains("\"Restart now\"", runner.Scripts.First());
        Assert.True(RanSystemEvents("restart"));
    }

    [Fact]
    public void No_grace_period_means_no_dialog()
    {
        Power(0).Shutdown();
        Assert.Equal(["tell application \"System Events\" to shut down"], runner.Scripts);
    }

    [Fact]
    public void Failed_shutdown_throws()
    {
        runner.Respond = (file, args) => file == Tools.Osascript && args[1].Contains("System Events")
            ? new(1, "", "Not authorized to send Apple events to System Events. (-1743)")
            : null;

        var error = Assert.Throws<InvalidOperationException>(() => Power().Shutdown());
        Assert.Contains("-1743", error.Message);
    }

    [Fact]
    public void Sleep_and_hibernate_use_pmset_sleepnow()
    {
        Power().Sleep();
        Power().Hibernate();
        Assert.Equal(2, runner.Calls.Count(c => c.SequenceEqual([Tools.Pmset, "sleepnow"])));
    }

    [Fact]
    public void Display_on_declares_user_activity()
    {
        Power().TurnDisplayOn();
        Assert.Equal([Tools.Caffeinate, "-u", "-t", "5"], Assert.Single(runner.Calls));
    }
}

public class MacCapabilityProbeTests
{
    [Theory]
    [InlineData("Now drawing from 'AC Power'\n -InternalBattery-0 (id=4653155)\t100%; charged; 0:00 remaining present: true\n", true)]
    [InlineData("Now drawing from 'Battery Power'\n -InternalBattery-0 (id=4653155)\t87%; discharging; 6:12 remaining present: true\n", false)]
    [InlineData("Now drawing from 'AC Power'\n", true)] // a desktop Mac has no battery line
    [InlineData("Now drawing from 'UPS Power'\n", false)]
    [InlineData("", false)]
    public void Reads_the_power_source(string output, bool onAc) =>
        Assert.Equal(onAc, MacCapabilityProbe.IsOnAcPower(output));

    [Fact]
    public void Probe_reports_mac_capabilities()
    {
        var runner = new FakeRunner { Respond = (file, _) => file == Tools.Pmset ? new(0, "Now drawing from 'AC Power'\n", "") : null };

        var caps = new MacCapabilityProbe(runner).Probe();

        Assert.Equal(new PowerCapabilities(ModernStandby: false, S3Sleep: true, Hibernate: true,
            WakeTimersOnAc: true, WakeTimersOnBattery: false, OnAcPower: true), caps);
        Assert.Equal([Tools.Pmset, "-g", "batt"], Assert.Single(runner.Calls));
    }

    [Fact]
    public void Probe_failure_is_not_ac()
    {
        var runner = new FakeRunner { Respond = (_, _) => new(1, "", "boom") };
        Assert.False(new MacCapabilityProbe(runner).Probe().OnAcPower);
    }
}

public class MacWakeVerifierTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 30, 7, 0, 30, TimeSpan.FromHours(3));

    const string Log = """
        2026-10-29 23:10:11 +0300 Sleep               	Entering Sleep state due to 'Idle Sleep':TCPKeepAlive=active Using Batt (Charge:80%) 2 secs
        2026-10-30 06:59:02 +0300 Wake Requests       	[*process=powerd request=UserWake deltaSecs=86340 wakeAt=2026-10-31 06:59:00 info="NapPilot"]
        2026-10-30 06:59:02 +0300 Wake                	Wake from Deep Idle [CDNVA] : due to RTC/Alarm Using AC (Charge:80%) 3 secs
        2026-10-30 06:59:03 +0300 Assertions          	PID 123(caffeinate) Created UserIsActive "caffeinate command-line tool" 00:00:00  id:0x0x0
        """;

    [Fact]
    public void Rtc_wake_in_the_last_ten_minutes_is_the_schedule() =>
        Assert.Equal(WakeCheck.WokeOnSchedule, MacWakeVerifier.Classify(Log, Now));

    [Fact]
    public void Other_wake_reason_is_another_source() =>
        Assert.Equal(WakeCheck.OtherWakeSource,
            MacWakeVerifier.Classify(Log.Replace("due to RTC/Alarm", "due to EC.LidOpen/Lid Open"), Now));

    [Fact]
    public void Later_dark_wake_wins() =>
        Assert.Equal(WakeCheck.OtherWakeSource, MacWakeVerifier.Classify(
            Log + "\r\n2026-10-30 06:59:40 +0300 DarkWake            \tDarkWake from Deep Idle [CDN] : due to SMC.OutboxNotEmpty/ Using AC\r\n",
            Now));

    [Fact]
    public void No_recent_wake_means_already_awake() =>
        Assert.Equal(WakeCheck.AlreadyAwake, MacWakeVerifier.Classify(Log, Now.AddMinutes(30)));

    [Fact]
    public void Failed_pmset_is_unknown()
    {
        var runner = new FakeRunner { Respond = (_, _) => new(1, "", "nope") };
        var verifier = new MacWakeVerifier(new ListLog(), runner, new FixedTime(Now, Zones.Istanbul));
        Assert.Equal(WakeCheck.Unknown, verifier.Check(Guid.NewGuid()));
    }
}

public sealed class MacLoginItemTests : IDisposable
{
    readonly TempDir agents = new();

    public void Dispose() => agents.Dispose();

    [Fact]
    public void Enabling_writes_a_run_at_load_agent_and_disabling_removes_it()
    {
        var item = new MacLoginItem("/Applications/NapPilot.app/Contents/MacOS/NapPilot", Path.Combine(agents.Path, "LaunchAgents"));
        Assert.False(item.IsEnabled);

        item.SetEnabled(true);
        Assert.True(item.IsEnabled);
        var text = File.ReadAllText(Path.Combine(agents.Path, "LaunchAgents", "com.nappilot.app.plist"));
        Assert.Contains("<key>Label</key>\n    <string>com.nappilot.app</string>", text);
        Assert.Contains("<string>/Applications/NapPilot.app/Contents/MacOS/NapPilot</string>\n      <string>--tray</string>", text);
        Assert.Contains("<key>RunAtLoad</key>\n    <true />", text);

        item.SetEnabled(false);
        Assert.False(item.IsEnabled);
        item.SetEnabled(false);
    }
}
