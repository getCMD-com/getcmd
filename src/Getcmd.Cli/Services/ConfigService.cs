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

    /// <summary>
    /// What an "ask" decision becomes when Claude Code runs in a mode that
    /// auto-approves, where a prompt would never be shown: "block", "allow" or "ask".
    /// </summary>
    public string? AskWhenAutoApproved { get; set; } = "block";
}

internal static class ConfigService
{
    /// <summary>
    /// Reads config.json, writing the defaults first if it does not exist yet.
    /// Never throws: an unreadable or invalid config is recorded in the error
    /// log and the defaults are used, so enforcement continues.
    /// </summary>
    public static Config Load(AppPaths paths)
    {
        try
        {
            return LoadStrict(paths);
        }
        catch (Exception ex)
        {
            ErrorLog.Write(paths, ex);
            return new Config();
        }
    }

    /// <summary>Like Load, but throws when config.json cannot be read or is invalid.</summary>
    public static Config LoadStrict(AppPaths paths)
    {
        if (!File.Exists(paths.ConfigFile))
        {
            var defaults = new Config();
            Save(paths, defaults);
            return defaults;
        }

        Config config;
        try
        {
            using var stream = File.OpenRead(paths.ConfigFile);
            config = JsonSerializer.Deserialize(stream, CliJsonContext.Default.Config)
                ?? throw new FormatException("config.json: expected a JSON object");
        }
        catch (JsonException ex)
        {
            throw new FormatException($"config.json: {ex.Message}", ex);
        }

        if (config.Actions is not null)
        {
            Overlay([], config.Actions);
        }

        if (!Names.TryParseAction(config.AskWhenAutoApproved ?? "block", out _))
        {
            throw new FormatException($"config.json: unknown askWhenAutoApproved \"{config.AskWhenAutoApproved}\" (block, allow or ask)");
        }

        return config;
    }

    /// <summary>The configured action for "ask" in an auto-approving mode; block unless set otherwise.</summary>
    public static Action AskWhenAutoApproved(Config config) =>
        Names.TryParseAction(config.AskWhenAutoApproved, out var action) ? action : Action.Block;

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
