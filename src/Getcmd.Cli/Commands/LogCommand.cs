using System.Globalization;
using System.Text;
using System.Text.Json;
using Getcmd.Cli.Services;

namespace Getcmd.Cli.Commands;

/// <summary>getcmd log: show recent decisions from log.db.</summary>
internal static class LogCommand
{
    // The command column never gets squeezed below this; the row wraps instead.
    private const int MinCommandWidth = 40;
    private const string Marker = "...";

    /// <param name="width">Terminal width; null means detect it, or no truncation when stdout is not a terminal.</param>
    public static int Run(int last, string? level, string? action, bool json, TextWriter stdout, int? width = null)
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

        width ??= ReferenceEquals(stdout, Console.Out) ? TerminalWidth() : null;
        var layout = new Layout(width, entries);

        stdout.WriteLine(layout.Row(layout.TimeHeader, "ACTION", "LEVEL", "RULE", "COMMAND"));
        foreach (var entry in entries)
        {
            stdout.WriteLine(layout.Row(
                layout.Time(entry.Ts), entry.Action, entry.Level, entry.RuleId ?? "-", entry.Command));
        }

        return 0;
    }

    // Column choices for one terminal width: narrower terminals get a shorter time
    // column (under 100) and lose the rule column (under 80).
    private sealed class Layout
    {
        private readonly int? _width;
        private readonly bool _shortTime;
        private readonly bool _showRule;
        private readonly int _ruleWidth;

        public Layout(int? width, List<LogEntry> entries)
        {
            _width = width;
            _shortTime = width < 100;
            _showRule = width is null or >= 80;
            _ruleWidth = 4;
            foreach (var entry in entries)
            {
                _ruleWidth = Math.Max(_ruleWidth, (entry.RuleId ?? "-").Length);
            }
        }

        public string TimeHeader => "TIME".PadRight(_shortTime ? 8 : 19);

        public string Time(string ts)
        {
            if (!DateTime.TryParse(ts, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var utc))
            {
                return ts.PadRight(_shortTime ? 8 : 19);
            }

            return utc.ToLocalTime().ToString(_shortTime ? "HH:mm:ss" : "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        public string Row(string time, string action, string level, string rule, string command)
        {
            var prefix = new StringBuilder()
                .Append(time).Append("  ")
                .Append(action.PadRight(6)).Append("  ")
                .Append(level.PadRight(11)).Append("  ");
            if (_showRule)
            {
                prefix.Append(rule.PadRight(_ruleWidth)).Append("  ");
            }

            command = command.ReplaceLineEndings(" ");
            if (_width is { } columns)
            {
                // Leave the last cell free so the terminal does not wrap the line itself.
                var room = Math.Max(columns - 1 - prefix.Length, MinCommandWidth);
                if (command.Length > room)
                {
                    command = string.Concat(command.AsSpan(0, room - Marker.Length), Marker);
                }
            }

            return prefix.Append(command).ToString();
        }
    }

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
