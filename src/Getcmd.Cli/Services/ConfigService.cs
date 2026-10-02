using System.Text.Json;
using Getcmd.Core;
using Action = Getcmd.Core.Action;

namespace Getcmd.Cli.Services;

/// <summary>Shape of ~/.getcmd/config.json. Missing keys keep these defaults.</summary>
internal sealed class Config
{
    public Dictionary<string, string>? Actions { get; set; } = new()
    {
        ["read"] = "allow",
        ["mutate"] = "allow",
        ["egress"] = "ask",
        ["secrets"] = "ask",
        ["destructive"] = "block",
    };

    public List<string>? BuildDirs { get; set; } =
        ["build", "dist", "bin", "obj", "node_modules", ".next", "target", "out"];

    public Dictionary<string, string>? HostTags { get; set; } = [];

    public List<string>? DisabledRules { get; set; } = [];

    public int LogRetentionDays { get; set; } = 30;
}

internal static class ConfigService
{
    /// <summary>Reads config.json, writing the defaults first if it does not exist yet.</summary>
    public static Config Load(AppPaths paths)
    {
        if (!File.Exists(paths.ConfigFile))
        {
            var defaults = new Config();
            Save(paths, defaults);
            return defaults;
        }

        using var stream = File.OpenRead(paths.ConfigFile);
        return JsonSerializer.Deserialize(stream, CliJsonContext.Default.Config) ?? new Config();
    }

    public static void Save(AppPaths paths, Config config)
    {
        paths.EnsureHome();
        File.WriteAllText(paths.ConfigFile, JsonSerializer.Serialize(config, CliJsonContext.Default.Config) + "\n");
    }

    public static Context ToContext(Config config, string cwd)
    {
        // Start from the defaults so a partial "actions" object still covers every level.
        var actions = new Dictionary<Level, Action>();
        Overlay(actions, new Config().Actions!);
        if (config.Actions is not null)
        {
            Overlay(actions, config.Actions);
        }

        return new Context(
            cwd,
            AppPaths.UserHome,
            config.BuildDirs ?? [],
            config.HostTags ?? new Dictionary<string, string>(),
            actions);
    }

    /// <summary>The builtin rules minus disabled ones; null when nothing is disabled.</summary>
    public static IReadOnlyList<Rule>? ActiveRules(Config config)
    {
        if (config.DisabledRules is not { Count: > 0 } disabled)
        {
            return null;
        }

        var rules = new List<Rule>(BuiltinRules.All.Count);
        foreach (var rule in BuiltinRules.All)
        {
            if (!disabled.Contains(rule.Id))
            {
                rules.Add(rule);
            }
        }

        return rules;
    }

    private static void Overlay(Dictionary<Level, Action> actions, Dictionary<string, string> configured)
    {
        foreach (var (key, value) in configured)
        {
            if (!Names.TryParseLevel(key, out var level))
            {
                throw new FormatException($"config.json: unknown level \"{key}\" in actions");
            }

            if (!Names.TryParseAction(value, out var action))
            {
                throw new FormatException($"config.json: unknown action \"{value}\" for {key}");
            }

            actions[level] = action;
        }
    }
}
