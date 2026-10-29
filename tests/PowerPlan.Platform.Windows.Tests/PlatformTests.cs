using System.Xml.Linq;
using PowerPlan.Core;
using PowerPlan.Platform.Windows;

namespace PowerPlan.Platform.Windows.Tests;

public class TaskXmlTests
{
    static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
    static readonly DateTimeOffset RunAt = new(2026, 10, 30, 7, 0, 0, TimeSpan.FromHours(3));

    static XElement Settings(XDocument doc) => doc.Root!.Element(Ns + "Settings")!;

    [Fact]
    public void Wake_schedule_wakes_the_computer()
    {
        var doc = TaskXml.Build(new Schedule { Name = "Morning", Action = PowerAction.Wake }, RunAt, @"C:\PowerPlan.exe", "PC\\user");
        Assert.Equal("true", Settings(doc).Element(Ns + "WakeToRun")!.Value);
    }

    [Theory]
    [InlineData(PowerAction.Shutdown)]
    [InlineData(PowerAction.Restart)]
    [InlineData(PowerAction.Sleep)]
    public void Other_actions_do_not_wake_the_computer(PowerAction action)
    {
        var doc = TaskXml.Build(new Schedule { Name = "x", Action = action }, RunAt, @"C:\PowerPlan.exe", "PC\\user");
        Assert.Equal("false", Settings(doc).Element(Ns + "WakeToRun")!.Value);
    }

    [Fact]
    public void Missed_runs_do_not_start_later_and_battery_does_not_block()
    {
        var settings = Settings(TaskXml.Build(new Schedule { Name = "x" }, RunAt, "a.exe", "u"));
        Assert.Equal("false", settings.Element(Ns + "StartWhenAvailable")!.Value);
        Assert.Equal("false", settings.Element(Ns + "DisallowStartIfOnBatteries")!.Value);
        Assert.Equal("false", settings.Element(Ns + "StopIfGoingOnBatteries")!.Value);
    }

    [Fact]
    public void Trigger_time_keeps_its_offset()
    {
        var doc = TaskXml.Build(new Schedule { Name = "x" }, RunAt, "a.exe", "u");
        Assert.Equal("2026-10-30T07:00:00+03:00", doc.Descendants(Ns + "StartBoundary").Single().Value);
    }

    [Fact]
    public void Action_runs_the_app_with_the_schedule_id()
    {
        var schedule = new Schedule { Name = "x" };
        var exec = TaskXml.Build(schedule, RunAt, @"C:\Apps\PowerPlan.App.exe", "u").Descendants(Ns + "Exec").Single();
        Assert.Equal(@"C:\Apps\PowerPlan.App.exe", exec.Element(Ns + "Command")!.Value);
        Assert.Equal($"--run {schedule.Id} --at {RunAt.ToUnixTimeSeconds()}", exec.Element(Ns + "Arguments")!.Value);
    }

    [Fact]
    public void Task_name_is_stable_per_schedule()
    {
        var id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
        Assert.Equal("NapPilot-0f8fad5bd9cb469fa16570867728950e", TaskXml.TaskName(id));
    }
}

public class SleepCatchGateTests
{
    static readonly DateTimeOffset T0 = new(2026, 10, 29, 11, 0, 0, TimeSpan.Zero);
    readonly SleepCatchGate gate = new(TimeSpan.FromSeconds(30));

    [Fact]
    public void Converts_once_when_a_wake_is_pending()
    {
        gate.OnAwake(T0);
        Assert.True(gate.ShouldConvert(T0.AddMinutes(5), wakePending: true));
        // The suspend broadcast that follows the display-off must not convert again.
        Assert.False(gate.ShouldConvert(T0.AddMinutes(5).AddMilliseconds(150), wakePending: true));
    }

    [Fact]
    public void Does_nothing_without_a_pending_wake()
    {
        gate.OnAwake(T0);
        Assert.False(gate.ShouldConvert(T0.AddMinutes(5), wakePending: false));
    }

    [Fact]
    public void Ignores_screen_flicker_right_after_waking()
    {
        // Seen in the 2026-10-29 test: display on, off 0.1 s later, then on again after a timer wake.
        gate.OnAwake(T0);
        Assert.False(gate.ShouldConvert(T0.AddMilliseconds(100), wakePending: true));
    }

    [Fact]
    public void Converts_again_after_the_next_wake()
    {
        gate.OnAwake(T0);
        Assert.True(gate.ShouldConvert(T0.AddMinutes(5), wakePending: true));
        gate.OnAwake(T0.AddHours(8));
        Assert.True(gate.ShouldConvert(T0.AddHours(9), wakePending: true));
    }
}

public class WindowsLoginItemTests : IDisposable
{
    // A throwaway key, so the test never touches the real Run key.
    readonly string keyPath = $@"Software\NapPilot.Tests\{Guid.NewGuid():N}";

    [Fact]
    public void Turns_on_and_off()
    {
        var item = new WindowsLoginItem(@"C:\Apps\NapPilot.exe", keyPath);
        Assert.False(item.IsEnabled);

        item.SetEnabled(true);
        Assert.True(item.IsEnabled);
        using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(keyPath)!)
            Assert.Equal(@"""C:\Apps\NapPilot.exe"" --tray", key.GetValue("NapPilot"));

        item.SetEnabled(false);
        Assert.False(item.IsEnabled);
    }

    [Fact]
    public void Entry_for_another_path_counts_as_off()
    {
        new WindowsLoginItem(@"C:\Old\NapPilot.exe", keyPath).SetEnabled(true);
        Assert.False(new WindowsLoginItem(@"C:\New\NapPilot.exe", keyPath).IsEnabled);
    }

    public void Dispose() => Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(@"Software\NapPilot.Tests", throwOnMissingSubKey: false);
}
