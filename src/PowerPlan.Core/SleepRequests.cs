using System.Globalization;

namespace PowerPlan.Core;

/// <summary>
/// Records when PowerPlan itself asked the PC to sleep, in a file so the tray app's sleep catcher sees
/// requests from the separate <c>--run</c> process too. Without it the catcher would ask to hibernate a
/// second time when the screen goes off, and that request could land after the next wake.
/// </summary>
public sealed class SleepRequestMarker(string path, TimeProvider time)
{
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(30);

    public void Mark() =>
        File.WriteAllText(path, time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));

    public bool IsRecent()
    {
        try
        {
            if (!File.Exists(path)) return false;
            var marked = DateTimeOffset.Parse(File.ReadAllText(path), CultureInfo.InvariantCulture);
            var age = time.GetUtcNow() - marked;
            return age >= TimeSpan.Zero && age < Window;
        }
        catch (Exception e) when (e is IOException or FormatException)
        {
            return false;
        }
    }
}

/// <summary>Marks sleep and hibernate requests before passing every action on.</summary>
public sealed class MarkingPowerController(IPowerController inner, SleepRequestMarker marker) : IPowerController
{
    public void Shutdown() => inner.Shutdown();
    public void Restart() => inner.Restart();

    public void Sleep()
    {
        marker.Mark();
        inner.Sleep();
    }

    public void Hibernate()
    {
        marker.Mark();
        inner.Hibernate();
    }

    public void TurnDisplayOn() => inner.TurnDisplayOn();
}
