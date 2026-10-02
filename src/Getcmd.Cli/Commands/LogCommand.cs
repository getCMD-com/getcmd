using System.Globalization;
using System.Text.Json;
using Getcmd.Cli.Services;

namespace Getcmd.Cli.Commands;

/// <summary>getcmd log: show recent decisions from log.db.</summary>
internal static class LogCommand
{
    public static int Run(int last, string? level, string? action, bool json, TextWriter stdout)
    {
        List<LogEntry> entries;
        using (var log = DecisionLog.Open(AppPaths.Resolve()))
        {
            entries = log.Query(Math.Max(last, 0), level?.ToLowerInvariant(), action?.ToLowerInvariant());
        }

        if (json)
        {
            stdout.WriteLine(JsonSerializer.Serialize(entries, CliJsonContext.Default.ListLogEntry));
            return 0;
        }

        if (entries.Count == 0)
        {
            stdout.WriteLine("No decisions logged.");
            return 0;
        }

        var ruleWidth = 4;
        foreach (var entry in entries)
        {
            ruleWidth = Math.Max(ruleWidth, (entry.RuleId ?? "-").Length);
        }

        var width = ReferenceEquals(stdout, Console.Out) ? TerminalWidth() : null;
        WriteRow(stdout, "TIME".PadRight(19), "ACTION", "LEVEL", "RULE", "COMMAND", ruleWidth, width);
        foreach (var entry in entries)
        {
            WriteRow(stdout, LocalTime(entry.Ts), entry.Action, entry.Level, entry.RuleId ?? "-", entry.Command, ruleWidth, width);
        }

        return 0;
    }

    private static void WriteRow(
        TextWriter stdout, string time, string action, string level, string rule, string command, int ruleWidth, int? width)
    {
        var prefix = $"{time}  {action,-6}  {level,-11}  {rule.PadRight(ruleWidth)}  ";
        command = command.ReplaceLineEndings(" ");

        // Leave the last column free so the terminal does not wrap.
        if (width is { } columns && prefix.Length + command.Length > columns - 1)
        {
            var room = Math.Max(columns - 1 - prefix.Length, 1);
            command = string.Concat(command.AsSpan(0, Math.Min(room - 1, command.Length)), "…");
        }

        stdout.WriteLine(prefix + command);
    }

    private static string LocalTime(string ts) =>
        DateTime.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var utc)
            ? utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            : ts.PadRight(19);

    private static int? TerminalWidth()
    {
        if (Console.IsOutputRedirected)
        {
            return null;
        }

        try
        {
            return Console.WindowWidth;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
