using System.Text.Json;
using Getcmd.Cli.Services;
using Getcmd.Core;
using Action = Getcmd.Core.Action;

namespace Getcmd.Cli.Commands;

/// <summary>getcmd rules: list the builtin rules, run the case file, disable or enable a rule.</summary>
internal static class RulesCommand
{
    private const string CasesFile = "rules/cases.json";
    private const string EmbeddedCases = "cases.json";

    // The fixture that rules/cases.json is written against.
    private const string CasesCwd = "/home/ally/src/app";
    private const string CasesHome = "/home/ally";
    private static readonly string[] CasesBuildDirs = ["build", "dist", "node_modules"];

    public static int List(TextWriter stdout)
    {
        var disabled = ConfigService.Load(AppPaths.Resolve()).DisabledRules ?? [];

        // Alternatives of one rule share an id; show each id once.
        var seen = new HashSet<string>();
        foreach (var rule in BuiltinRules.All)
        {
            if (seen.Add(rule.Id))
            {
                var mark = disabled.Contains(rule.Id) ? "  (disabled)" : "";
                stdout.WriteLine($"{rule.Id,-21} {Names.Of(rule.Level),-12} {rule.Reason}{mark}");
            }
        }

        return 0;
    }

    public static int Test(TextWriter stdout)
    {
        var cases = LoadCases(out var source);
        var actions = new Dictionary<Level, Action>();
        var failed = 0;

        foreach (var item in cases)
        {
            var ctx = new Context(
                item.Cwd ?? CasesCwd,
                CasesHome,
                CasesBuildDirs,
                item.HostTags ?? new Dictionary<string, string>(),
                actions);
            var decision = RuleEngine.Evaluate(item.Command, ctx);

            if (Names.TryParseLevel(item.Level, out var level) && decision.Level == level && decision.RuleId == item.Rule)
            {
                stdout.WriteLine($"pass  {item.Command}");
            }
            else
            {
                failed++;
                stdout.WriteLine(
                    $"FAIL  {item.Command}\n"
                    + $"      expected {item.Level.ToLowerInvariant()} / {item.Rule ?? "no rule"},"
                    + $" got {Names.Of(decision.Level)} / {decision.RuleId ?? "no rule"}");
            }
        }

        stdout.WriteLine($"{cases.Count - failed} passed, {failed} failed ({source})");
        return failed == 0 ? 0 : 1;
    }

    public static int SetDisabled(string id, bool disable, TextWriter stdout, TextWriter stderr)
    {
        var known = false;
        foreach (var rule in BuiltinRules.All)
        {
            known |= rule.Id == id;
        }

        if (!known)
        {
            stderr.WriteLine($"getcmd: unknown rule \"{id}\"; see getcmd rules list");
            return 1;
        }

        // Strict: saving over a config that failed to parse would wipe the user's settings.
        var paths = AppPaths.Resolve();
        var config = ConfigService.LoadStrict(paths);
        var disabled = config.DisabledRules ??= [];

        if (disable == disabled.Contains(id))
        {
            stdout.WriteLine($"{id} is already {(disable ? "disabled" : "enabled")}; nothing changed");
            return 0;
        }

        if (disable)
        {
            disabled.Add(id);
        }
        else
        {
            disabled.RemoveAll(existing => existing == id);
        }

        ConfigService.Save(paths, config);
        stdout.WriteLine($"{(disable ? "Disabled" : "Enabled")} {id} in {paths.ConfigFile}");
        return 0;
    }

    internal static Stream OpenEmbeddedCases() =>
        typeof(RulesCommand).Assembly.GetManifestResourceStream(EmbeddedCases)
        ?? throw new InvalidOperationException("embedded cases.json is missing");

    private static List<RuleCase> LoadCases(out string source)
    {
        var local = Path.Combine(Environment.CurrentDirectory, CasesFile);
        var useLocal = File.Exists(local);
        source = useLocal ? CasesFile : "embedded cases";

        using var stream = useLocal ? File.OpenRead(local) : OpenEmbeddedCases();
        return JsonSerializer.Deserialize(stream, CliJsonContext.Default.ListRuleCase) ?? [];
    }
}
