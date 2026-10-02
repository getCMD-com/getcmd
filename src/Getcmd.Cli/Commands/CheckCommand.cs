using System.Diagnostics;
using System.Text.Json;
using Getcmd.Cli.Services;
using Getcmd.Core;
using Action = Getcmd.Core.Action;

namespace Getcmd.Cli.Commands;

/// <summary>getcmd check: classify one command line and show the decision.</summary>
internal static class CheckCommand
{
    private const string Agent = "cli";
    private const int MaxRawWidth = 48;

    public static int Run(string command, string? cwd, bool json, TextWriter stdout)
    {
        var started = Stopwatch.GetTimestamp();
        var paths = AppPaths.Resolve();
        var config = ConfigService.Load(paths);
        var ctx = ConfigService.ToContext(config, cwd ?? Environment.CurrentDirectory);
        var decision = RuleEngine.Evaluate(command, ctx, ConfigService.ActiveRules(config));
        DecisionLog.Record(paths, config, Agent, null, ctx.Cwd, command, decision, Stopwatch.GetElapsedTime(started));

        if (json)
        {
            stdout.WriteLine(JsonSerializer.Serialize(DecisionJson.From(decision), CliJsonContext.Default.DecisionJson));
        }
        else
        {
            Print(decision, stdout);
        }

        return decision.Action switch
        {
            Action.Allow => 0,
            Action.Ask => 1,
            _ => 2,
        };
    }

    private static void Print(Decision decision, TextWriter stdout)
    {
        var color = ReferenceEquals(stdout, Console.Out)
            && !Console.IsOutputRedirected
            && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));

        WriteLevel(stdout, Names.Of(decision.Level).ToUpperInvariant(), decision.Level, color);
        stdout.WriteLine($"  {Names.Of(decision.Action),-6}  {decision.RuleId ?? "-"}");

        var width = 0;
        PartDecision? top = null;
        foreach (var part in decision.Parts)
        {
            width = Math.Clamp(part.Raw.Length, width, MaxRawWidth);
            if (top is null && part.Level == decision.Level)
            {
                top = part;
            }
        }

        foreach (var part in decision.Parts)
        {
            stdout.Write($"  {Fit(part.Raw, width)}  ");
            WriteLevel(stdout, Names.Of(part.Level), part.Level, color);

            // The part that decided the outcome gets its reason; the others just name their rule.
            stdout.WriteLine($"  {(ReferenceEquals(part, top) ? part.Reason : part.RuleId ?? "-")}");
        }
    }

    private static void WriteLevel(TextWriter stdout, string text, Level level, bool color)
    {
        if (color)
        {
            Console.ForegroundColor = level switch
            {
                Level.Destructive => ConsoleColor.Red,
                Level.Secrets => ConsoleColor.Magenta,
                Level.Egress => ConsoleColor.Yellow,
                Level.Mutate => ConsoleColor.Cyan,
                _ => ConsoleColor.Green,
            };
        }

        stdout.Write(text.PadRight(11));

        if (color)
        {
            Console.ResetColor();
        }
    }

    private static string Fit(string text, int width)
    {
        text = text.ReplaceLineEndings(" ");
        return text.Length <= width ? text.PadRight(width) : string.Concat(text.AsSpan(0, width - 1), "…");
    }
}
