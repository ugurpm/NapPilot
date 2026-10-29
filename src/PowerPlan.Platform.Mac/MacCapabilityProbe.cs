using PowerPlan.Core;

namespace PowerPlan.Platform.Mac;

public sealed class MacCapabilityProbe : ICapabilityProbe
{
    readonly ICommandRunner runner;

    public MacCapabilityProbe() : this(ProcessCommandRunner.Instance)
    {
    }

    internal MacCapabilityProbe(ICommandRunner runner) => this.runner = runner;

    public PowerCapabilities Probe()
    {
        var battery = runner.Run(Tools.Pmset, "-g", "batt");
        return new PowerCapabilities(
            ModernStandby: false,
            S3Sleep: true,
            Hibernate: true,
            WakeTimersOnAc: true,
            // Apple's "Schedule your Mac to turn on or off" help says a Mac laptop must be connected to its power
            // adapter for a scheduled start up or wake, so the advisor tells laptop users to stay plugged in.
            // Desktops always report AC, so this never warns them. Not measured on hardware.
            WakeTimersOnBattery: false,
            OnAcPower: battery.Succeeded && IsOnAcPower(battery.Output));
    }

    /// <summary><c>pmset -g batt</c> starts with "Now drawing from 'AC Power'" or "'Battery Power'" (also 'UPS Power').</summary>
    internal static bool IsOnAcPower(string pmsetBatt) => pmsetBatt.Contains("'AC Power'", StringComparison.Ordinal);
}
