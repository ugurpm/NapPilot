using PowerPlan.Core;

namespace PowerPlan.Core.Tests;

public class SleepRequestTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "powerplan-tests-" + Guid.NewGuid().ToString("N"));
    readonly FixedTime time = new(new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc);
    readonly SleepRequestMarker marker;

    public SleepRequestTests()
    {
        Directory.CreateDirectory(dir);
        marker = new SleepRequestMarker(Path.Combine(dir, "sleep-request.txt"), time);
    }

    [Fact]
    public void Nothing_marked_is_not_recent()
    {
        Assert.False(marker.IsRecent());
    }

    [Fact]
    public void Mark_is_recent_within_the_window_only()
    {
        marker.Mark();

        // The catcher saw the screen go off 1.4 s after the app's own request in the 2026-10-29 test.
        time.Now = time.Now.AddSeconds(1.4);
        Assert.True(marker.IsRecent());

        time.Now = time.Now + SleepRequestMarker.Window;
        Assert.False(marker.IsRecent());
    }

    [Fact]
    public void Marker_is_shared_through_the_file()
    {
        marker.Mark();
        var otherProcess = new SleepRequestMarker(Path.Combine(dir, "sleep-request.txt"), time);
        Assert.True(otherProcess.IsRecent());
    }

    [Fact]
    public void Corrupt_file_is_not_recent()
    {
        File.WriteAllText(Path.Combine(dir, "sleep-request.txt"), "not a date");
        Assert.False(marker.IsRecent());
    }

    [Fact]
    public void Controller_marks_sleep_and_hibernate_only()
    {
        var inner = new RecordingPower();
        var power = new MarkingPowerController(inner, marker);

        power.Shutdown();
        power.TurnDisplayOn();
        Assert.False(marker.IsRecent());

        power.Hibernate();
        Assert.True(marker.IsRecent());
        Assert.Equal(["Shutdown", "TurnDisplayOn", "Hibernate"], inner.Calls);
    }

    public void Dispose() => Directory.Delete(dir, recursive: true);
}
