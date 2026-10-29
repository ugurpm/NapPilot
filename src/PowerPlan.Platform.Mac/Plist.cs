using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PowerPlan.Platform.Mac;

/// <summary>Writes Apple XML property lists, the format launchd reads.</summary>
internal static class Plist
{
    public static string LaunchAgentsDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents");

    /// <summary>Values may be string, int, long, bool, a string sequence (array) or a nested dictionary.</summary>
    public static XDocument Build(IEnumerable<KeyValuePair<string, object>> root) =>
        new(new XDeclaration("1.0", "UTF-8", null),
            new XDocumentType("plist", "-//Apple//DTD PLIST 1.0//EN", "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null),
            new XElement("plist", new XAttribute("version", "1.0"), Dict(root)));

    public static string ToText(XDocument doc)
    {
        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false), NewLineChars = "\n" };
        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, settings)) doc.Save(writer);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    static XElement Dict(IEnumerable<KeyValuePair<string, object>> entries) =>
        new("dict", entries.SelectMany(e => new[] { new XElement("key", e.Key), Value(e.Value) }));

    static XElement Value(object value) => value switch
    {
        string s => new XElement("string", s),
        bool b => new XElement(b ? "true" : "false"),
        int or long => new XElement("integer", value),
        IEnumerable<KeyValuePair<string, object>> dict => Dict(dict),
        IEnumerable<string> items => new XElement("array", items.Select(i => new XElement("string", i))),
        _ => throw new ArgumentException($"Unsupported plist value {value.GetType()}"),
    };
}

/// <summary>The per-user launchd domain, <c>gui/&lt;uid&gt;</c>.</summary>
internal sealed class LaunchdDomain(ICommandRunner runner)
{
    string? domain;

    public string Name => domain ??= $"gui/{Uid()}";

    public string Target(string label) => $"{Name}/{label}";

    public CommandResult Bootstrap(string plistPath) => runner.Run(Tools.Launchctl, "bootstrap", Name, plistPath);

    public CommandResult Bootout(string label) => runner.Run(Tools.Launchctl, "bootout", Target(label));

    /// <summary>Exit code 0 when the job is loaded; the text says <c>state = running</c> while it runs.</summary>
    public CommandResult Print(string label) => runner.Run(Tools.Launchctl, "print", Target(label));

    string Uid()
    {
        var result = runner.Run(Tools.Id, "-u");
        return result.Succeeded && int.TryParse(result.Output.Trim(), out var uid)
            ? uid.ToString()
            : throw new InvalidOperationException($"Could not read the user id: {result.Text}");
    }
}
