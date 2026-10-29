using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Xml.Linq;
using PowerPlan.Core;

namespace PowerPlan.Platform.Windows;

/// <summary>Builds the Task Scheduler XML for one schedule run.</summary>
public static class TaskXml
{
    static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    public static string TaskName(Guid scheduleId) => $"PowerPlan-{scheduleId:N}";

    public static XDocument Build(Schedule schedule, DateTimeOffset runAt, string exePath, string userId)
    {
        XElement El(string name, params object[] content) => new(Ns + name, content);

        return new XDocument(
            new XDeclaration("1.0", "UTF-16", null),
            El("Task", new XAttribute("version", "1.2"),
                El("RegistrationInfo",
                    El("Author", "PowerPlan"),
                    El("Description", $"{schedule.Action}: {schedule.Name}")),
                El("Triggers",
                    // One-off trigger with an explicit offset; the app arms the next occurrence after each run.
                    El("TimeTrigger",
                        El("StartBoundary", runAt.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture)),
                        El("Enabled", "true"))),
                El("Principals",
                    El("Principal", new XAttribute("id", "Author"),
                        El("UserId", userId),
                        El("LogonType", "InteractiveToken"),
                        El("RunLevel", "LeastPrivilege"))),
                El("Settings",
                    El("MultipleInstancesPolicy", "IgnoreNew"),
                    El("DisallowStartIfOnBatteries", "false"),
                    El("StopIfGoingOnBatteries", "false"),
                    // A missed shutdown must not fire later when the user is back at the PC.
                    El("StartWhenAvailable", "false"),
                    El("WakeToRun", schedule.Action == PowerAction.Wake ? "true" : "false"),
                    El("ExecutionTimeLimit", "PT10M"),
                    El("Enabled", "true")),
                El("Actions", new XAttribute("Context", "Author"),
                    El("Exec",
                        El("Command", exePath),
                        El("Arguments", $"--run {schedule.Id}")))));
    }
}

public sealed class TaskSchedulerBackend(string exePath, ILog log) : IScheduleBackend
{
    public void Register(Schedule schedule, DateTimeOffset runAt)
    {
        var xml = TaskXml.Build(schedule, runAt, exePath, System.Security.Principal.WindowsIdentity.GetCurrent().Name);
        var file = Path.Combine(Path.GetTempPath(), $"{TaskXml.TaskName(schedule.Id)}.xml");
        File.WriteAllText(file, xml.Declaration + Environment.NewLine + xml, Encoding.Unicode);
        try
        {
            var (exit, output) = Schtasks("/Create", "/TN", TaskXml.TaskName(schedule.Id), "/XML", file, "/F");
            if (exit != 0) throw new InvalidOperationException($"schtasks /Create failed ({exit}): {output}");
        }
        finally
        {
            File.Delete(file);
        }
    }

    public void Unregister(Guid scheduleId)
    {
        var (exit, output) = Schtasks("/Delete", "/TN", TaskXml.TaskName(scheduleId), "/F");
        if (exit != 0) log.Write($"schtasks /Delete {TaskXml.TaskName(scheduleId)} returned {exit} (fine if it did not exist): {output.Trim()}");
    }

    static (int ExitCode, string Output) Schtasks(params string[] args)
    {
        var start = new ProcessStartInfo("schtasks.exe")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args) start.ArgumentList.Add(arg);

        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}
